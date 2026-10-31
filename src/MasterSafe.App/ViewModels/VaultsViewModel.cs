// File:    VaultsViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// The Vaults page: shows where the vault files are, lets the user open another vault file, move the current
/// vault aside and start a new one, and permanently delete old vaults.
/// </summary>
public sealed partial class VaultsViewModel : ViewModelBase
{
    private readonly AppServices? _services;
    private readonly IReadOnlyList<ISecretProvider> _providers;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _confirmingNew;

    /// <summary>Creates the page. With no services it shows that it is unavailable.</summary>
    public VaultsViewModel(AppServices? services = null, IEnumerable<ISecretProvider>? providers = null)
    {
        _services = services;
        _providers = providers?.ToList() ?? [];
        Refresh();
    }

    /// <summary>Known vault files.</summary>
    public ObservableCollection<VaultRowViewModel> Vaults { get; } = [];

    /// <summary>True when vault management is available.</summary>
    public bool IsAvailable => _services is not null;

    /// <summary>Folder where the app keeps its files.</summary>
    public string DataFolder => _services?.Paths.DataFolder ?? string.Empty;

    /// <summary>The vault file in use.</summary>
    public string ActivePath => _services?.Catalog.ActivePath ?? string.Empty;

    /// <summary>Raised after the active vault changed or was deleted. The shell locks and reloads the login screen.</summary>
    public Func<Task>? ActiveVaultChanged { get; set; }

    /// <summary>Reloads the list from disk.</summary>
    public void Refresh()
    {
        Vaults.Clear();
        if (_services is not null)
        {
            foreach (var entry in _services.Catalog.List())
            {
                Vaults.Add(new VaultRowViewModel(entry));
            }
        }

        OnPropertyChanged(nameof(ActivePath));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(DataFolder));
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (_services is null)
        {
            return;
        }

        var path = await _services.Dialogs.OpenFileAsync("Open a vault file", "Vault files", "*.sauth");
        if (path is not null)
        {
            await SwitchToAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRowAsync(VaultRowViewModel? row)
    {
        if (row is not null && !row.IsActive)
        {
            await SwitchToAsync(row.Path);
        }
    }

    [RelayCommand]
    private void ShowInFolder(VaultRowViewModel? row)
    {
        if (row is not null)
        {
            _services?.Dialogs.ShowInFolder(row.Path);
        }
    }

    [RelayCommand]
    private void RequestStartNew() => ConfirmingNew = true;

    [RelayCommand]
    private void CancelStartNew() => ConfirmingNew = false;

    [RelayCommand]
    private async Task ConfirmStartNewAsync()
    {
        ConfirmingNew = false;
        if (_services is null)
        {
            return;
        }

        try
        {
            var archived = _services.Catalog.StartNewVault();
            Status = archived is null
                ? "A new vault will be created when you set a master password."
                : $"Your old vault was kept in the archive ({Path.GetFileName(archived)}). Set a master password to create the new one.";
            await NotifyChangedAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not move the old vault: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RequestDelete(VaultRowViewModel? row)
    {
        if (row is { CanDelete: true })
        {
            row.IsConfirmingDelete = true;
        }
    }

    [RelayCommand]
    private void CancelDelete(VaultRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsConfirmingDelete = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync(VaultRowViewModel? row)
    {
        if (row is not { CanDelete: true } || _services is null)
        {
            return;
        }

        try
        {
            await RemoveHardwareCredentialsAsync(row.Path);
            _services.Catalog.Delete(row.Path);
            Status = $"Deleted {row.Name}.";
            var wasActive = row.IsActive;
            Refresh();
            if (wasActive)
            {
                await NotifyChangedAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Could not delete it: {ex.Message}";
            row.IsConfirmingDelete = false;
        }
    }

    [RelayCommand]
    private void Forget(VaultRowViewModel? row)
    {
        if (row is { CanForget: true } && _services is not null)
        {
            _services.Catalog.Forget(row.Path);
            Status = $"{row.Name} was removed from the list. The file was not touched.";
            Refresh();
        }
    }

    private async Task SwitchToAsync(string path)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            _services.Catalog.SetActive(path);
            Status = $"Opened {Path.GetFileName(path)}. Unlock it with its master password or security key.";
            await NotifyChangedAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not open that file: {ex.Message}";
        }
    }

    private async Task NotifyChangedAsync()
    {
        Refresh();
        if (ActiveVaultChanged is not null)
        {
            await ActiveVaultChanged();
        }
    }

    // Windows Hello credentials belong to the vault's slots. When a vault is deleted its credentials are removed too.
    private async Task RemoveHardwareCredentialsAsync(string path)
    {
        try
        {
            var vault = await new FileVaultRepository(path).LoadAsync();
            foreach (var slot in vault?.Slots ?? [])
            {
                var provider = _providers.FirstOrDefault(p => p.SlotType == slot.Type);
                if (provider is not null)
                {
                    await provider.RemoveAsync(slot.Id);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // The file is unreadable, so there is nothing to clean up.
        }
    }
}

/// <summary>One vault file in the list.</summary>
public sealed partial class VaultRowViewModel(VaultEntry entry) : ObservableObject
{
    [ObservableProperty]
    private bool _isConfirmingDelete;

    /// <summary>Full path of the file.</summary>
    public string Path { get; } = entry.Path;

    /// <summary>File name.</summary>
    public string Name { get; } = entry.Name;

    /// <summary>True when this is the vault in use.</summary>
    public bool IsActive { get; } = entry.IsActive;

    /// <summary>Short description of the file.</summary>
    public string Description { get; } = entry.Kind switch
    {
        VaultKind.Archived => $"Archived, last changed {entry.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
        VaultKind.External => $"Opened from another place, last changed {entry.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
        _ => $"Main vault, last changed {entry.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
    };

    /// <summary>Size in kilobytes, for display.</summary>
    public string Size { get; } = $"{Math.Max(1, entry.Size / 1024)} KB";

    /// <summary>Badge text.</summary>
    public string Badge { get; } = entry.IsActive ? "In use" : string.Empty;

    /// <summary>Another vault can be opened.</summary>
    public bool CanOpen { get; } = !entry.IsActive;

    /// <summary>Only vaults inside the app's folder can be deleted from the app.</summary>
    public bool CanDelete { get; } = entry.Kind != VaultKind.External;

    /// <summary>Vaults opened from elsewhere can be removed from the list but never deleted.</summary>
    public bool CanForget { get; } = entry.Kind == VaultKind.External;
}
