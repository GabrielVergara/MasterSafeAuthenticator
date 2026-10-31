// File:    RecoveryViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// The Recovery page. A recovery key lets you get your data back if you forget the master password: you save
/// the key file somewhere safe, and the app can then write recovery backups that only that key can open.
/// </summary>
public sealed partial class RecoveryViewModel : ViewModelBase
{
    private readonly AppServices? _services;
    private readonly Func<VaultContents>? _contents;

    [ObservableProperty]
    private bool _hasKey;

    [ObservableProperty]
    private string _keyInfo = string.Empty;

    [ObservableProperty]
    private string _lastBackupInfo = "No recovery backup yet.";

    [ObservableProperty]
    private string _passphrase = string.Empty;

    [ObservableProperty]
    private string _confirmPassphrase = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the page. With no services it shows that it is unavailable.</summary>
    public RecoveryViewModel(AppServices? services = null, Func<VaultContents>? contents = null)
    {
        _services = services;
        _contents = contents;
        Refresh();
    }

    /// <summary>True when the page can be used.</summary>
    public bool IsAvailable => _services is not null;

    /// <summary>True when this operating system provides the post-quantum algorithms the recovery key needs.</summary>
    public bool IsSupported { get; } = PostQuantumProviderFactory.CreateHybrid().IsAvailable;

    /// <summary>Shown when the algorithms are missing.</summary>
    public bool IsNotSupported => !IsSupported;

    /// <summary>True when a key can be created now.</summary>
    public bool CanCreateKey => IsAvailable && IsSupported && !HasKey;

    /// <summary>Reads the saved recovery key information.</summary>
    public void Refresh()
    {
        var settings = _services?.SettingsStore.Load();
        HasKey = !string.IsNullOrEmpty(settings?.RecoveryPublicKey);
        KeyInfo = HasKey
            ? $"Recovery key set up on {Format(settings!.RecoveryKeyCreatedUtc)}. Key id: {ShortId(settings.RecoveryKeyId)}."
            : "No recovery key yet.";
        LastBackupInfo = string.IsNullOrEmpty(settings?.LastRecoveryBackupUtc)
            ? "No recovery backup yet."
            : $"Last recovery backup: {Format(settings.LastRecoveryBackupUtc)}.";
        OnPropertyChanged(nameof(CanCreateKey));
        OnPropertyChanged(nameof(IsAvailable));
    }

    [RelayCommand]
    private async Task CreateKeyAsync()
    {
        if (_services is null)
        {
            return;
        }

        if (Passphrase.Length < RecoveryKeyService.MinPassphraseLength)
        {
            Status = $"The passphrase must be at least {RecoveryKeyService.MinPassphraseLength} characters.";
            return;
        }

        if (Passphrase != ConfirmPassphrase)
        {
            Status = "The passphrases do not match.";
            return;
        }

        IsBusy = true;
        try
        {
            var created = await Task.Run(() => _services.RecoveryKeys.Create(Passphrase));

            // The key file must be saved before the app starts relying on it, otherwise nothing could open the backups.
            var suggested = $"MasterSafe-recovery-key-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
            var path = await _services.Dialogs.SaveFileAsync("Save your recovery key file", suggested, "msrk");
            if (path is null)
            {
                Status = "The recovery key was not saved, so it was not set up. Try again and choose where to save it.";
                return;
            }

            await File.WriteAllBytesAsync(path, created.FileBytes);

            var settings = _services.SettingsStore.Load();
            settings.RecoveryPublicKey = Convert.ToBase64String(created.PublicKey);
            settings.RecoveryKeyId = created.KeyId;
            settings.RecoveryKeyCreatedUtc = created.CreatedUtc;
            _services.SettingsStore.Save(settings);

            Passphrase = ConfirmPassphrase = string.Empty;
            Status = $"Saved to {path}. Keep that file and its passphrase somewhere other than this computer, for example a USB stick in a safe place.";
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            Status = $"Could not create the recovery key: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (_services is null || _contents is null)
        {
            return;
        }

        var settings = _services.SettingsStore.Load();
        if (string.IsNullOrEmpty(settings.RecoveryPublicKey))
        {
            Status = "Create a recovery key first.";
            return;
        }

        var folder = await _services.Dialogs.PickFolderAsync("Choose where to save the recovery backup");
        if (folder is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var path = await _services.RecoveryBackups.ExportContentsAsync(
                _contents(), Convert.FromBase64String(settings.RecoveryPublicKey), new LocalFolderBackupProvider(folder));

            settings = _services.SettingsStore.Load();
            settings.LastRecoveryBackupUtc = DateTimeOffset.UtcNow.ToString("O");
            _services.SettingsStore.Save(settings);

            Status = $"Recovery backup saved to {path}. It contains your accounts and passwords, encrypted so that only your recovery key can open it.";
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or PlatformNotSupportedException)
        {
            Status = $"Could not create the backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Format(string? iso)
        => DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "an unknown date";

    private static string ShortId(string? id)
        => string.IsNullOrEmpty(id) ? "unknown" : id.Replace("=", string.Empty, StringComparison.Ordinal)[..Math.Min(8, id.Length)];
}
