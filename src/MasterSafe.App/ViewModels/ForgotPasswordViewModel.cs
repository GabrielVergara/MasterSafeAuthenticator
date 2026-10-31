// File:    ForgotPasswordViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// "I forgot my master password": the ways forward. The old vault is never deleted. You can restore from a recovery
/// backup, start a new vault (the old one is moved to the archive and can be opened later if the password turns up),
/// or open a different vault file.
/// </summary>
public sealed partial class ForgotPasswordViewModel : ViewModelBase
{
    private readonly AppServices? _services;
    private readonly IVaultService? _vault;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _showRestore;

    [ObservableProperty]
    private bool _confirmingNew;

    [ObservableProperty]
    private string _backupFilePath = string.Empty;

    [ObservableProperty]
    private string _keyFilePath = string.Empty;

    [ObservableProperty]
    private string _keyPassphrase = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Creates the panel. With no services only the explanation is shown.</summary>
    public ForgotPasswordViewModel(AppServices? services = null, IVaultService? vault = null)
    {
        _services = services;
        _vault = vault;
    }

    /// <summary>True when recovery actions are available.</summary>
    public bool IsAvailable => _services is not null && _vault is not null;

    /// <summary>Unlocks the vault with the new master password after a restore.</summary>
    public Func<string, Task>? Restored { get; set; }

    /// <summary>Raised after the active vault changed so the shell reloads the login screen.</summary>
    public Func<Task>? ActiveVaultChanged { get; set; }

    /// <summary>Clears typed values and closes the panel.</summary>
    public void Reset()
    {
        IsOpen = false;
        ShowRestore = false;
        ConfirmingNew = false;
        BackupFilePath = KeyFilePath = KeyPassphrase = NewPassword = ConfirmPassword = string.Empty;
        Status = null;
    }

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => Reset();

    [RelayCommand]
    private void ToggleRestore() => ShowRestore = !ShowRestore;

    [RelayCommand]
    private void RequestStartNew() => ConfirmingNew = true;

    [RelayCommand]
    private void CancelStartNew() => ConfirmingNew = false;

    [RelayCommand]
    private async Task BrowseBackupAsync()
    {
        if (_services is not null)
        {
            BackupFilePath = await _services.Dialogs.OpenFileAsync("Choose the recovery backup", "Backup files", "*.sauth") ?? BackupFilePath;
        }
    }

    [RelayCommand]
    private async Task BrowseKeyAsync()
    {
        if (_services is not null)
        {
            KeyFilePath = await _services.Dialogs.OpenFileAsync("Choose your recovery key file", "Recovery key files", "*.msrk") ?? KeyFilePath;
        }
    }

    [RelayCommand]
    private async Task StartNewAsync()
    {
        ConfirmingNew = false;
        if (_services is null)
        {
            return;
        }

        try
        {
            _services.Catalog.StartNewVault();
            Reset();
            if (ActiveVaultChanged is not null)
            {
                await ActiveVaultChanged();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not move the old vault: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenVaultFileAsync()
    {
        if (_services is null)
        {
            return;
        }

        var path = await _services.Dialogs.OpenFileAsync("Open a vault file", "Vault files", "*.sauth");
        if (path is null)
        {
            return;
        }

        try
        {
            _services.Catalog.SetActive(path);
            Reset();
            if (ActiveVaultChanged is not null)
            {
                await ActiveVaultChanged();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not open that file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (_services is null || _vault is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BackupFilePath) || string.IsNullOrWhiteSpace(KeyFilePath) || KeyPassphrase.Length == 0)
        {
            Status = "Choose the recovery backup and the recovery key file, and enter the key's passphrase.";
            return;
        }

        if (NewPassword.Length < LoginViewModel.MinPasswordLength)
        {
            Status = $"The new master password must be at least {LoginViewModel.MinPasswordLength} characters.";
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            Status = "The new passwords do not match.";
            return;
        }

        IsBusy = true;
        Status = "Opening the recovery backup...";
        byte[]? privateKey = null;
        try
        {
            privateKey = _services.RecoveryKeys.OpenPrivateKey(await File.ReadAllBytesAsync(BackupFileGuard(KeyFilePath)), KeyPassphrase);

            var folder = Path.GetDirectoryName(Path.GetFullPath(BackupFilePath))!;
            var contents = await _services.RecoveryBackups.ImportContentsAsync(
                Path.GetFileName(BackupFilePath), privateKey, new LocalFolderBackupProvider(folder));

            // Only now, with the data in hand, move the old vault aside and create the new one.
            _services.Catalog.StartNewVault();
            await _vault.SaveContentsAsync(NewPassword, contents);

            var password = NewPassword;
            Reset();
            if (Restored is not null)
            {
                await Restored(password);
            }
        }
        catch (InvalidPasswordException)
        {
            Status = "That key file and passphrase cannot open this backup. Check that you chose the recovery key that was in use when the backup was made.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            Status = $"Could not restore: {ex.Message}";
        }
        finally
        {
            if (privateKey is not null)
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }

            IsBusy = false;
        }
    }

    private static string BackupFileGuard(string path)
    {
        var full = Path.GetFullPath(path);
        if (new FileInfo(full).Length > 64 * 1024)
        {
            throw new InvalidDataException("That file is too large to be a recovery key.");
        }

        return full;
    }
}
