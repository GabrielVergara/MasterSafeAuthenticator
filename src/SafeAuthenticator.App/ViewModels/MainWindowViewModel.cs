// File:    MainWindowViewModel.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// Root view model: owns navigation between login and the main shell.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private object _currentPage;

    private readonly IVaultService? _vault;
    private string? _sessionPassword;

    public LoginViewModel Login { get; } = new();

    public DashboardViewModel Dashboard { get; }

    public SettingsViewModel Settings { get; } = new();

    public BackupViewModel Backup { get; } = new();

    public ImportViewModel Import { get; } = new();

    public HelpViewModel Help { get; } = new();

    /// <summary>Raised when the user asks to open the About dialog.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>Creates the shell. With no vault service a demo account is shown (design/test mode).</summary>
    public MainWindowViewModel(IVaultService? vault = null)
    {
        _vault = vault;
        Dashboard = new DashboardViewModel();
        _currentPage = Login;

        if (_vault is null)
        {
            Login.UnlockRequested += (_, _) => Unlock();
            Dashboard.Load(
            [
                new OtpAccount { Issuer = "SafeAuthenticator", Label = "demo@local", Secret = "JBSWY3DPEHPK3PXP" },
            ]);
        }
        else
        {
            Login.UnlockRequested += async (_, password) => await UnlockVaultAsync(password);
            _ = InitializeLoginAsync();
        }

        // Wire backup export to the live dashboard accounts.
        Backup.AccountsProvider = () => Dashboard.Accounts.Select(a => a.Account).ToList();
    }

    private async Task InitializeLoginAsync()
    {
        Login.IsCreateMode = !await _vault!.ExistsAsync();
    }

    /// <summary>Creates (first run) or opens the vault and shows the dashboard.</summary>
    public async Task UnlockVaultAsync(string password)
    {
        Login.IsBusy = true;
        try
        {
            if (!await _vault!.ExistsAsync())
            {
                await _vault.CreateAsync(password);
            }

            var accounts = await _vault.OpenAsync(password);
            _sessionPassword = password;
            Dashboard.Load(accounts);
            Login.MasterPassword = string.Empty;
            Login.ConfirmPassword = string.Empty;
            Login.IsCreateMode = false;
            Unlock();
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

    [RelayCommand]
    private void Unlock()
    {
        IsUnlocked = true;
        CurrentPage = Dashboard;
    }

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = Dashboard;

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
        _sessionPassword = null;
        CurrentPage = Login;
    }
}
