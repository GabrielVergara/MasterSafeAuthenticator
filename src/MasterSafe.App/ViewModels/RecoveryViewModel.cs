// File:    RecoveryViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>The passphrase MasterSafe chose, shown until the user confirms they wrote it down.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneratedPassphrase))]
    private string _generatedPassphrase = string.Empty;

    /// <summary>True while a generated passphrase is shown.</summary>
    public bool HasGeneratedPassphrase => GeneratedPassphrase.Length > 0;

    // Letters and digits that cannot be mistaken for each other when written down (no 0, 1, I, L, O).
    private const string PassphraseAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Makes a random passphrase for the recovery key: 30 characters from a 31-letter alphabet in six groups,
    /// for example <c>K7QF2-M9XTD-W3HPA-8RCNB-4ZJYE-6VGUS</c> (about 148 bits).
    /// </summary>
    public static string GeneratePassphrase()
    {
        var builder = new StringBuilder(35);
        for (var i = 0; i < 30; i++)
        {
            if (i > 0 && i % 5 == 0)
            {
                builder.Append('-');
            }

            builder.Append(PassphraseAlphabet[RandomNumberGenerator.GetInt32(PassphraseAlphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>Forgets any passphrase that is still on screen or in the form (used when the vault is locked).</summary>
    public void ClearSecrets()
    {
        Passphrase = ConfirmPassphrase = GeneratedPassphrase = string.Empty;
        TrustedKeyFingerprint = null;
    }

    /// <summary>The fingerprint of the recovery key the open vault trusts; null until it has one.</summary>
    public string? TrustedKeyFingerprint { get; set; }

    /// <summary>Saves the vault after the trusted key changed. Set by the main view model.</summary>
    public Func<Task>? SaveRequested { get; set; }

    /// <summary>The fingerprint stored in the vault for a recovery key.</summary>
    public static string KeyFingerprint(string publicKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(publicKey))).ToLowerInvariant();

    private const string UntrustedKeyStatus = "This recovery key is not the one this vault trusts, so no backup was written. The settings file may have been changed. Create a new recovery key.";

    private bool IsUntrusted(string publicKey)
        => TrustedKeyFingerprint is not null && TrustedKeyFingerprint != KeyFingerprint(publicKey);

    /// <summary>Checks the key before a backup is encrypted to it. A vault with no trusted key yet starts trusting this one.</summary>
    private async Task<bool> TrustKeyAsync(string publicKey)
    {
        if (TrustedKeyFingerprint is null)
        {
            await RememberKeyAsync(publicKey);
            return true;
        }

        return !IsUntrusted(publicKey);
    }

    private async Task RememberKeyAsync(string publicKey)
    {
        TrustedKeyFingerprint = KeyFingerprint(publicKey);
        if (SaveRequested is not null)
        {
            await SaveRequested();
        }
    }

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
        var key = settings?.RecoveryPublicKey;
        var untrusted = !string.IsNullOrEmpty(key) && IsUntrusted(key);
        HasKey = !string.IsNullOrEmpty(key) && !untrusted;
        KeyInfo = HasKey
            ? $"Recovery key set up on {Format(settings!.RecoveryKeyCreatedUtc)}. Key id: {ShortId(settings.RecoveryKeyId)}."
            : untrusted
                ? "The recovery key in the settings does not match the one this vault trusts. It may have been changed. Create a new recovery key."
                : "No recovery key yet.";
        LastBackupInfo = string.IsNullOrEmpty(settings?.LastRecoveryBackupUtc)
            ? "No recovery backup yet."
            : $"Last recovery backup: {Format(settings.LastRecoveryBackupUtc)}.";
        LoadAutoSettings(settings);
        OnPropertyChanged(nameof(CanCreateKey));
        OnPropertyChanged(nameof(IsAvailable));
    }

    /// <summary>
    /// The easy way: MasterSafe chooses a strong passphrase, then asks where to save the key file. The passphrase is
    /// shown afterwards so it can be written down. Nothing is set up if the file is not saved.
    /// </summary>
    [RelayCommand]
    private async Task CreateKeyAutomaticallyAsync()
    {
        if (_services is null || HasKey)
        {
            return;
        }

        var generated = GeneratePassphrase();
        Passphrase = ConfirmPassphrase = generated;
        GeneratedPassphrase = generated;

        await CreateKeyAsync();

        if (!HasKey)
        {
            Passphrase = ConfirmPassphrase = GeneratedPassphrase = string.Empty;
        }
        else
        {
            Status = $"{Status} The passphrase below is shown only now. Write it down and keep it apart from the key file.";
        }
    }

    /// <summary>Hides the generated passphrase once the user has written it down.</summary>
    [RelayCommand]
    private void DismissGeneratedPassphrase() => GeneratedPassphrase = string.Empty;

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
            await RememberKeyAsync(settings.RecoveryPublicKey);

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

        if (!await TrustKeyAsync(settings.RecoveryPublicKey))
        {
            Status = UntrustedKeyStatus;
            Refresh();
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
