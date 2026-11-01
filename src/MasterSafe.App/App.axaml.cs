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
        AutoHideReveal.Install();

        // The language is chosen before any window exists, so even the first screen is in it.
        Loc.Install();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            try
            {
                Loc.Use(new SettingsStore(AppPaths.Default.SettingsPath).Load().Language);
            }
            catch (Exception)
            {
                Loc.Use("System");
            }
        }

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
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(paths.DataFolder);
        }
        else
        {
            Directory.CreateDirectory(paths.DataFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Free protections: harder DLL planting, no error-report dumps of the unlocked vault, data folder for this user only.
        ProcessHardening.Apply(Path.GetFileName(Environment.ProcessPath));
        ProcessHardening.RestrictToCurrentUser(paths.DataFolder);

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
        // Really closes the program, whatever the tray setting. Locking first wipes the session key.
        void ExitProgram()
        {
            _shuttingDown = true;
            viewModel.LockNow();
            _tray?.Dispose();
            desktop.Shutdown();
        }

        viewModel.ExitRequested = ExitProgram;

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
                case TrayCommands.LockVault:
                    viewModel.LockNow();
                    break;
                case TrayCommands.About:
                    if (!window.IsVisible)
                    {
                        window.Show();
                    }

                    window.ShowAbout();
                    break;
                case TrayCommands.Exit:
                    ExitProgram();
                    break;
            }
        };

        // Lock the vault when Windows is locked or goes to sleep, and optionally when the window is minimized.
        _session = new SessionMonitor();
        _session.SessionLocking += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.Settings.LockOnSessionLock)
            {
                viewModel.LockNow();
            }
        });
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Window.WindowStateProperty
                && window.WindowState == Avalonia.Controls.WindowState.Minimized
                && viewModel.Settings.LockOnMinimize)
            {
                viewModel.LockNow();
            }
        };

        // Keep the window out of screenshots and screen sharing unless the user turned that off.
        // The system-wide lock shortcut needs the real window, so it is applied once the window is open.
        var hotkey = new GlobalLockHotkey(window, viewModel.LockNow);
        void ApplyHotkey() => viewModel.Settings.LockHotkeyNote = hotkey.Apply(viewModel.Settings.GlobalLockHotkey)
            ? null
            : Loc.T("Ctrl+Alt+L could not be turned on. Another program may be using it.");

        window.Opened += (_, _) =>
        {
            WindowCaptureProtection.Apply(window, viewModel.Settings.BlockScreenCapture);
            ApplyHotkey();
        };
        viewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.BlockScreenCapture))
            {
                WindowCaptureProtection.Apply(window, viewModel.Settings.BlockScreenCapture);
            }
            else if (e.PropertyName == nameof(SettingsViewModel.GlobalLockHotkey))
            {
                ApplyHotkey();
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

        desktop.ShutdownRequested += (_, _) =>
        {
            _tray?.Dispose();
            _session?.Dispose();
        };
    }

    private SessionMonitor? _session;
    private bool _shuttingDown;

    /// <summary>The black and white theme with strong borders. It is built on the dark theme.</summary>
    public static readonly ThemeVariant HighContrastVariant = new("HighContrast", ThemeVariant.Dark);

    /// <summary>Applies the selected theme (Light/Dark/System/High contrast).</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null)
        {
            return;
        }

        // Themes belong to the UI thread; from any other thread the change is passed on to it.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTheme(theme));
            return;
        }

        // "Follow the system" also follows the system's high contrast setting.
        var osWantsHighContrast = theme == AppTheme.System
            && Current.PlatformSettings?.GetColorValues().ContrastPreference == Avalonia.Platform.ColorContrastPreference.High;

        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.HighContrast => HighContrastVariant,
            _ when osWantsHighContrast => HighContrastVariant,
            _ => ThemeVariant.Default,
        };
    }
}
