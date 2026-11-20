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

    /// <summary>Pieces of a printed recovery key read from photos so far.</summary>
    private readonly HashSet<string> _paperPieces = new(StringComparer.Ordinal);

    /// <summary>Pieces typed by hand, for codes that could not be read.</summary>
    [ObservableProperty]
    private string _paperKeyText = string.Empty;

    /// <summary>True once the user started reading a printed key.</summary>
    [ObservableProperty]
    private bool _showPaperKey;

    /// <summary>What was read from the printed key so far.</summary>
    [ObservableProperty]
    private string? _paperKeyStatus;

    /// <summary>Reads QR codes from images. Replaced in tests.</summary>
    public Func<string, IReadOnlyList<string>> ReadCodes { get; set; } = path => new MasterSafe.QR.QrCodeImporter().DecodeAllTexts(path);

    private bool HasPaperKey => _paperPieces.Count > 0 || !string.IsNullOrWhiteSpace(PaperKeyText);

    private byte[] PaperKeyBytes() => PaperKey.Join(_paperPieces.Concat([PaperKeyText]));

    /// <summary>Key part files added so far, by path, so the same file added twice counts once.</summary>
    private readonly Dictionary<string, byte[]> _keyParts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What was added from key parts so far.</summary>
    [ObservableProperty]
    private string? _keyPartsStatus;

    [RelayCommand]
    private async Task AddKeyPartAsync()
    {
        if (_services is null)
        {
            return;
        }

        var path = await _services.Dialogs.OpenFileAsync("Choose a part of your recovery key", "Recovery key parts", "*.msrkpart");
        if (path is null)
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(path);
            if (new FileInfo(full).Length > 256 * 1024)
            {
                throw new InvalidDataException("This is not a MasterSafe recovery key part.");
            }

            _keyParts[full] = await File.ReadAllBytesAsync(full);
            KeySplitter.Combine(_keyParts.Values);
            KeyPartsStatus = "The recovery key was rebuilt from its parts.";
        }
        catch (InvalidDataException ex)
        {
            KeyPartsStatus = Loc.T(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            KeyPartsStatus = $"That file could not be read: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReadPaperKeyAsync()
    {
        if (_services is null)
        {
            return;
        }

        ShowPaperKey = true;
        var path = await _services.Dialogs.OpenFileAsync("Choose a photo or scan of the printed key", "Images", "*.png", "*.jpg", "*.jpeg", "*.bmp");
        if (path is null)
        {
            return;
        }

        try
        {
            var found = ReadCodes(path).Where(t => t.StartsWith(PaperKey.Prefix, StringComparison.Ordinal)).ToList();
            _paperPieces.UnionWith(found);
            PaperKeyBytes();
            PaperKeyStatus = "The whole recovery key was read.";
        }
        catch (InvalidDataException ex)
        {
            PaperKeyStatus = _paperPieces.Count == 0
                ? "No code of a printed recovery key was found in that picture. Try a sharper photo with the whole page in it, or type the text below."
                : string.Join(' ', Loc.T($"{_paperPieces.Count} codes read so far."), Loc.T(ex.Message), Loc.T("Add a photo of another page, or type the missing text below."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or MasterSafe.QR.QrImportException)
        {
            PaperKeyStatus = $"That picture could not be read: {ex.Message}";
        }
    }

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
        BackupFilePath = KeyFilePath = KeyPassphrase = NewPassword = ConfirmPassword = PaperKeyText = string.Empty;
        _paperPieces.Clear();
        _keyParts.Clear();
        KeyPartsStatus = null;
        ShowPaperKey = false;
        PaperKeyStatus = null;
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

        if (string.IsNullOrWhiteSpace(BackupFilePath) || (string.IsNullOrWhiteSpace(KeyFilePath) && !HasPaperKey && _keyParts.Count == 0) || KeyPassphrase.Length == 0)
        {
            Status = "Choose the recovery backup and the recovery key file (or read a printed key, or add its parts), and enter the key's passphrase.";
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
            var keyFile = !string.IsNullOrWhiteSpace(KeyFilePath) ? await File.ReadAllBytesAsync(BackupFileGuard(KeyFilePath))
                : HasPaperKey ? PaperKeyBytes()
                : KeySplitter.Combine(_keyParts.Values);
            privateKey = _services.RecoveryKeys.OpenPrivateKey(keyFile, KeyPassphrase);

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
