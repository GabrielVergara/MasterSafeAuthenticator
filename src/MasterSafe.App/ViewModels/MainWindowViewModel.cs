// File:    MainWindowViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.Platform;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Root view model: owns navigation, the unlocked vault session, quick unlock and persistence.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>Credential Manager target holding the DPAPI-wrapped vault key.</summary>
    public const string QuickUnlockTarget = "MasterSafe.QuickUnlock";

    private readonly IVaultService? _vault;
    private readonly ICredentialStore? _credentials;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private byte[]? _sessionKey;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private object _currentPage;

    public LoginViewModel Login { get; } = new();

    public DashboardViewModel Dashboard { get; }

    public PasswordsViewModel Passwords { get; } = new();

    public SettingsViewModel Settings { get; } = new();

    public BackupViewModel Backup { get; } = new();

    public ImportViewModel Import { get; } = new();

    public AccountEditorViewModel AccountEditor { get; } = new();

    public HelpViewModel Help { get; } = new();

    /// <summary>Raised when the user asks to open the About dialog.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>
    /// Creates the shell. With no vault service a demo account is shown (design/test mode).
    /// </summary>
    public MainWindowViewModel(IVaultService? vault = null, ICredentialStore? credentials = null)
    {
        _vault = vault;
        _credentials = credentials;
        Dashboard = new DashboardViewModel();
        _currentPage = Login;

        if (_vault is null)
        {
            Login.UnlockRequested += (_, _) => Unlock();
            Dashboard.Load(
            [
                new OtpAccount { Issuer = "MasterSafe", Label = "demo@local", Secret = "JBSWY3DPEHPK3PXP" },
            ]);
        }
        else
        {
            Login.UnlockRequested += async (_, password) => await UnlockVaultAsync(password);
            Login.QuickUnlockRequested += async (_, _) => await QuickUnlockAsync();
            _ = InitializeLoginAsync();
        }

        Backup.AccountsProvider = () => Dashboard.Accounts.Select(a => a.Account).ToList();

        Dashboard.AccountsChanged += async (_, _) => await PersistAsync();
        Passwords.EntriesChanged += async (_, _) => await PersistAsync();
        Import.AccountImported += (_, account) =>
        {
            Dashboard.Add(account);
            CurrentPage = Dashboard;
        };
        AccountEditor.AccountSaved += (_, account) =>
        {
            Dashboard.Add(account);
            CurrentPage = Dashboard;
        };
        Settings.ChangePasswordHandler = ChangeMasterPasswordAsync;
    }

    /// <summary>Creates (first run) or opens the vault with the master password and shows the dashboard.</summary>
    public async Task UnlockVaultAsync(string password)
    {
        Login.IsBusy = true;
        try
        {
            if (!await _vault!.ExistsAsync())
            {
                await _vault.CreateAsync(password);
            }

            var key = await _vault.DeriveKeyAsync(password);
            var contents = await _vault.OpenWithKeyAsync(key);
            _sessionKey = key;
            ApplyQuickUnlockPreference(key);
            CompleteUnlock(contents);
        }
        catch (InvalidPasswordException)
        {
            Login.Error = "Incorrect master password.";
        }
        catch (Exception ex)
        {
            Login.Error = $"Could not open vault: {ex.Message}";
        }
        finally
        {
            Login.IsBusy = false;
        }
    }

    /// <summary>Opens the vault using the key stored in Windows Credential Manager.</summary>
    public async Task QuickUnlockAsync()
    {
        var key = _credentials?.Read(QuickUnlockTarget);
        if (key is null)
        {
            Login.HasQuickUnlock = false;
            Login.Error = "No saved quick unlock. Enter your master password.";
            return;
        }

        Login.IsBusy = true;
        try
        {
            var contents = await _vault!.OpenWithKeyAsync(key);
            _sessionKey = key;
            CompleteUnlock(contents);
        }
        catch (InvalidPasswordException)
        {
            // The master password changed elsewhere; the stored key is stale.
            _credentials!.Delete(QuickUnlockTarget);
            Login.HasQuickUnlock = false;
            Login.Error = "Quick unlock expired. Enter your master password.";
        }
        catch (Exception ex)
        {
            Login.Error = $"Could not open vault: {ex.Message}";
        }
        finally
        {
            Login.IsBusy = false;
        }
    }

    /// <summary>Saves accounts and passwords to the encrypted vault (no-op without a vault or session).</summary>
    public async Task PersistAsync()
    {
        if (_vault is null || _sessionKey is null)
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            await _vault.SaveWithKeyAsync(_sessionKey, BuildContents());
        }
        catch (Exception ex)
        {
            Dashboard.StatusMessage = $"Could not save vault: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Changes the master password. Returns null on success or an error message.
    /// The quick unlock credential is revoked and, if it was enabled, replaced with the new key.
    /// </summary>
    public async Task<string?> ChangeMasterPasswordAsync(string currentPassword, string newPassword)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        await _saveLock.WaitAsync();
        try
        {
            await _vault.DeriveKeyAsync(currentPassword);
            await _vault.SaveContentsAsync(newPassword, BuildContents());
            var newKey = await _vault.DeriveKeyAsync(newPassword);

            var hadQuickUnlock = _credentials?.Read(QuickUnlockTarget) is not null;
            _credentials?.Delete(QuickUnlockTarget);
            if (hadQuickUnlock && _credentials is { IsSupported: true })
            {
                _credentials.Save(QuickUnlockTarget, newKey);
            }

            _sessionKey = newKey;
            return null;
        }
        catch (InvalidPasswordException)
        {
            return "Current password is incorrect.";
        }
        catch (Exception ex)
        {
            return $"Could not change password: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    [RelayCommand]
    private void Unlock()
    {
        IsUnlocked = true;
        CurrentPage = Dashboard;
    }

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = Dashboard;

    [RelayCommand]
    private void ShowPasswords() => CurrentPage = Passwords;

    [RelayCommand]
    private void ShowAddAccount() => CurrentPage = AccountEditor;

    [RelayCommand]
    private void ShowSettings() => CurrentPage = Settings;

    [RelayCommand]
    private void ShowBackup() => CurrentPage = Backup;

    [RelayCommand]
    private void ShowImport() => CurrentPage = Import;

    [RelayCommand]
    private void ShowHelp() => CurrentPage = Help;

    [RelayCommand]
    private void ShowAbout() => AboutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Lock()
    {
        IsUnlocked = false;
        _sessionKey = null;
        Login.HasQuickUnlock = _credentials?.Read(QuickUnlockTarget) is not null;
        CurrentPage = Login;
    }

    private async Task InitializeLoginAsync()
    {
        Login.IsCreateMode = !await _vault!.ExistsAsync();
        Login.HasQuickUnlock = !Login.IsCreateMode && _credentials?.Read(QuickUnlockTarget) is not null;
    }

    private VaultContents BuildContents() => new()
    {
        Accounts = Dashboard.Accounts.Select(a => a.Account).ToList(),
        Passwords = Passwords.Snapshot(),
    };

    private void ApplyQuickUnlockPreference(byte[] key)
    {
        if (_credentials is not { IsSupported: true })
        {
            return;
        }

        if (Login.RememberMe)
        {
            _credentials.Save(QuickUnlockTarget, key);
        }
        else
        {
            _credentials.Delete(QuickUnlockTarget);
        }
    }

    private void CompleteUnlock(VaultContents contents)
    {
        Dashboard.Load(contents.Accounts);
        Passwords.Load(contents.Passwords);
        Login.MasterPassword = string.Empty;
        Login.ConfirmPassword = string.Empty;
        Login.IsCreateMode = false;
        Login.Error = null;
        Unlock();
    }
}
