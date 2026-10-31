// File:    App.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.App.Views;
using MasterSafe.Backup;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using MasterSafe.Platform;

namespace MasterSafe.App;

/// <summary>Avalonia application root.</summary>
public partial class App : Application
{
    private ISystemTray? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The splash is the lifetime's main window until start-up work finishes, so the app
            // does not shut down when it closes.
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();

            var mainWindow = new MainWindow();
            var (vault, services) = CreateServices(mainWindow);
            var mainViewModel = new MainWindowViewModel(vault, PlatformServices.CreateUnlockProviders(), startIdleTimer: true, services: services);
            mainWindow.DataContext = mainViewModel;
            ApplyTheme(mainViewModel.Settings.SelectedTheme);

            SetupTray(desktop, mainWindow, mainViewModel);
            _ = ShowMainWindowAfterSplashAsync(desktop, splash, mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowMainWindowAfterSplashAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        MainWindow mainWindow)
    {
        await Task.Delay(SplashDuration);

        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }

    private static readonly TimeSpan SplashDuration = TimeSpan.FromMilliseconds(2000);

    private static (IVaultService Vault, AppServices Services) CreateServices(Window owner)
    {
        var paths = AppPaths.Default;
        Directory.CreateDirectory(paths.DataFolder);

        var settings = new SettingsStore(paths.SettingsPath);
        var repository = new SwitchableVaultRepository(paths.DefaultVaultPath);
        var catalog = new VaultCatalog(paths, repository, settings);
        var encryption = new AesGcmEncryptionService();
        var passwords = new MasterPasswordService();

        var vault = new VaultService(repository, encryption, passwords);
        var hybrid = PostQuantumProviderFactory.CreateHybrid();
        var services = new AppServices(
            paths,
            catalog,
            settings,
            new WindowFileDialogs(() => owner),
            new RecoveryKeyService(encryption, passwords, hybrid),
            new RecipientBackupService(encryption, hybrid));

        return (vault, services);
    }

    private void SetupTray(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window,
        MainWindowViewModel viewModel)
    {
        _tray = new AvaloniaSystemTray();
        _tray.Show(AppInfo.ProductName, TrayCommands.DefaultMenu);

        _tray.MenuItemClicked += (_, command) =>
        {
            switch (command)
            {
                case TrayCommands.ShowWindow:
                    window.Show();
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                    window.Activate();
                    break;
                case TrayCommands.HideWindow:
                    window.Hide();
                    break;
                case TrayCommands.About:
                    if (!window.IsVisible)
                    {
                        window.Show();
                    }

                    window.ShowAbout();
                    break;
                case TrayCommands.Exit:
                    _shuttingDown = true;
                    _tray?.Dispose();
                    desktop.Shutdown();
                    break;
            }
        };

        // Keep running in the tray when the preference is enabled: closing the window hides it...
        window.Closing += (_, e) =>
        {
            if (viewModel.Settings.MinimizeToTray && _tray.IsSupported && !_shuttingDown)
            {
                e.Cancel = true;
                window.Hide();
            }
        };

        // ...and so does minimizing it, which also removes its button from the taskbar.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty
                && window.WindowState == Avalonia.Controls.WindowState.Minimized
                && viewModel.Settings.MinimizeToTray && _tray.IsSupported && !_shuttingDown)
            {
                window.Hide();
            }
        };

        desktop.ShutdownRequested += (_, _) => _tray?.Dispose();
    }

    private bool _shuttingDown;

    /// <summary>Applies the selected theme (Light/Dark/System).</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null)
        {
            return;
        }

        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
