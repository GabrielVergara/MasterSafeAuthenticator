// File:    App.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MasterSafe.App.ViewModels;
using MasterSafe.App.Views;
using MasterSafe.Crypto;
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

            var mainViewModel = new MainWindowViewModel(CreateVault(), CreateCredentialStore());
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

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

    private static ICredentialStore CreateCredentialStore()
        => OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new HeadlessCredentialStore();

    private static IVaultService CreateVault()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MasterSafe");
        Directory.CreateDirectory(folder);

        return new VaultService(
            new FileVaultRepository(Path.Combine(folder, "vault.sauth")),
            new AesGcmEncryptionService(),
            new MasterPasswordService());
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

        // Minimize to tray instead of closing when the preference is enabled.
        window.Closing += (_, e) =>
        {
            if (viewModel.Settings.MinimizeToTray && _tray.IsSupported && !_shuttingDown)
            {
                e.Cancel = true;
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
