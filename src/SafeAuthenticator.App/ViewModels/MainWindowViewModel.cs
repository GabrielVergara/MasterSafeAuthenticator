using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeAuthenticator.Core;

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

    public LoginViewModel Login { get; } = new();

    public DashboardViewModel Dashboard { get; }

    public SettingsViewModel Settings { get; } = new();

    public BackupViewModel Backup { get; } = new();

    public ImportViewModel Import { get; } = new();

    public HelpViewModel Help { get; } = new();

    /// <summary>Raised when the user asks to open the About dialog.</summary>
    public event EventHandler? AboutRequested;

    public MainWindowViewModel()
    {
        Dashboard = new DashboardViewModel();
        _currentPage = Login;
        Login.UnlockRequested += (_, _) => Unlock();

        // Seed a demo account so the dashboard renders meaningfully on first run.
        Dashboard.Load(
        [
            new OtpAccount { Issuer = "SafeAuthenticator", Label = "demo@local", Secret = "JBSWY3DPEHPK3PXP" },
        ]);

        // Wire backup export to the live dashboard accounts.
        Backup.AccountsProvider = () => Dashboard.Accounts.Select(a => a.Account).ToList();
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
        CurrentPage = Login;
    }
}
