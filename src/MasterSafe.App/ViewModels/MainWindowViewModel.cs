// File:    MainWindowViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System;
using System.Security.Cryptography;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Root view model: owns navigation, the unlocked vault session, unlock methods, auto-lock and persistence.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IVaultService? _vault;
    private readonly IReadOnlyList<ISecretProvider> _providers;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly DispatcherTimer? _idleTimer;
    private byte[]? _sessionKey;
    private DateTimeOffset _lastActivity;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private object _currentPage;

    /// <summary>Name of the navigation section matching <see cref="CurrentPage"/> (drives the sidebar highlight).</summary>
    [ObservableProperty]
    private string _activeSection = string.Empty;

    partial void OnCurrentPageChanged(object value)
        => ActiveSection = value switch
        {
            DashboardViewModel => "Dashboard",
            PasswordsViewModel => "Passwords",
            AccountEditorViewModel => "Add",
            ImportViewModel => "Import",
            BackupViewModel => "Backup",
            SettingsViewModel => "Settings",
            HelpViewModel => "Help",
            _ => string.Empty,
        };

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
    /// <param name="vault">The vault, or null for demo mode.</param>
    /// <param name="providers">Hardware or OS unlock methods offered on this platform.</param>
    /// <param name="clock">Time source (replaced in tests).</param>
    /// <param name="startIdleTimer">Starts the auto-lock timer; needs the UI thread.</param>
    public MainWindowViewModel(
        IVaultService? vault = null,
        IEnumerable<ISecretProvider>? providers = null,
        Func<DateTimeOffset>? clock = null,
        bool startIdleTimer = false)
    {
        _vault = vault;
        _providers = providers?.ToList() ?? [];
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lastActivity = _clock();
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
            Login.MethodUnlockRequested += async (_, provider) => await UnlockWithMethodAsync(provider);
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
        Settings.AddMethodHandler = AddUnlockMethodAsync;
        Settings.RemoveMethodHandler = RemoveUnlockMethodAsync;

        if (startIdleTimer && Dispatcher.UIThread.CheckAccess())
        {
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _idleTimer.Tick += (_, _) => LockIfIdle();
            _idleTimer.Start();
        }
    }

    /// <summary>Records user activity so the auto-lock countdown restarts.</summary>
    public void NotifyActivity() => _lastActivity = _clock();

    /// <summary>Locks the vault when there has been no activity for the configured time.</summary>
    public void LockIfIdle()
    {
        if (IsUnlocked && _vault is not null
            && _clock() - _lastActivity >= TimeSpan.FromMinutes(Math.Max(1, Settings.AutoLockMinutes)))
        {
            Lock();
        }
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
            await CompleteUnlockWithKeyAsync(key);
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

    /// <summary>Opens the vault with a hardware or OS unlock method such as Windows Hello.</summary>
    public async Task UnlockWithMethodAsync(ISecretProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Login.IsBusy = true;
        try
        {
            var key = await _vault!.UnlockWithProviderAsync(provider);
            await CompleteUnlockWithKeyAsync(key);
        }
        catch (InvalidPasswordException)
        {
            Login.Error = $"{provider.DisplayName} could not unlock the vault. Use your master password.";
        }
        catch (OperationCanceledException)
        {
            Login.Error = $"{provider.DisplayName} was cancelled.";
        }
        catch (Exception ex)
        {
            Login.Error = $"{provider.DisplayName} failed: {ex.Message}";
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
            if (_sessionKey is not null)
            {
                await _vault.SaveWithKeyAsync(_sessionKey, BuildContents());
            }
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
    /// Other unlock methods (Windows Hello, security keys) keep working.
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
            await _vault.RotateMasterPasswordAsync(currentPassword, newPassword);
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

    /// <summary>Adds a hardware or OS unlock method. Returns null on success or an error message.</summary>
    public async Task<string?> AddUnlockMethodAsync(ISecretProvider provider)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        await _saveLock.WaitAsync();
        try
        {
            await _vault.AddSlotAsync(_sessionKey, provider, provider.DisplayName);
            await RefreshUnlockMethodsAsync();
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not add {provider.DisplayName}: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Removes an unlock method. Returns null on success or an error message.</summary>
    public async Task<string?> RemoveUnlockMethodAsync(string slotId)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        await _saveLock.WaitAsync();
        try
        {
            var slots = await _vault.ListSlotsAsync();
            var slot = slots.FirstOrDefault(s => s.Id == slotId);
            var provider = _providers.FirstOrDefault(p => p.SlotType == slot?.Type);
            await _vault.RemoveSlotAsync(_sessionKey, slotId, provider);
            await RefreshUnlockMethodsAsync();
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not remove it: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Reloads the lists of unlock methods shown on the login screen and in Settings.</summary>
    public async Task RefreshUnlockMethodsAsync()
    {
        Login.UnlockMethods.Clear();
        Settings.UnlockMethods.Clear();
        Settings.AddableMethods.Clear();

        if (_vault is null || !await _vault.ExistsAsync())
        {
            return;
        }

        var slots = await _vault.ListSlotsAsync();
        foreach (var slot in slots)
        {
            Settings.UnlockMethods.Add(new UnlockMethodRowViewModel(slot));
        }

        foreach (var provider in _providers)
        {
            if (!await provider.IsAvailableAsync())
            {
                continue;
            }

            if (slots.Any(s => s.Type == provider.SlotType))
            {
                Login.UnlockMethods.Add(new UnlockMethodViewModel(provider));
            }
            else
            {
                Settings.AddableMethods.Add(new AddMethodOptionViewModel(provider));
            }
        }
    }

    [RelayCommand]
    private void Unlock()
    {
        IsUnlocked = true;
        NotifyActivity();
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

    /// <summary>Locks the vault: wipes the session key and removes decrypted data from memory-backed view models.</summary>
    [RelayCommand]
    private void Lock()
    {
        if (_sessionKey is not null)
        {
            CryptographicOperations.ZeroMemory(_sessionKey);
            _sessionKey = null;
        }

        IsUnlocked = false;
        CurrentPage = Login;

        // The key is gone, so clearing these cannot trigger a save.
        Dashboard.Load([]);
        Passwords.Load([]);
        Passwords.ClearFormCommand.Execute(null);
        AccountEditor.Secret = string.Empty;
        Import.ManualUri = string.Empty;
        Backup.MasterPassword = string.Empty;
        Backup.WebDavPassword = string.Empty;
        Settings.CurrentPassword = string.Empty;
        Settings.NewPassword = string.Empty;
        Settings.ConfirmNewPassword = string.Empty;
        Login.MasterPassword = string.Empty;
        Login.ConfirmPassword = string.Empty;
        Login.Error = null;

        if (_vault is not null)
        {
            _ = RefreshUnlockMethodsAsync();
        }
    }

    private async Task InitializeLoginAsync()
    {
        Login.IsCreateMode = !await _vault!.ExistsAsync();
        await RefreshUnlockMethodsAsync();
    }

    private VaultContents BuildContents() => new()
    {
        Accounts = Dashboard.Accounts.Select(a => a.Account).ToList(),
        Passwords = Passwords.Snapshot(),
    };

    private async Task CompleteUnlockWithKeyAsync(byte[] key)
    {
        try
        {
            var contents = await _vault!.OpenWithKeyAsync(key);
            _sessionKey = key;
            CompleteUnlock(contents);
            await RefreshUnlockMethodsAsync();
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
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
