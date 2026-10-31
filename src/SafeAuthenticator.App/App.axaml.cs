// File:    App.axaml.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SafeAuthenticator.App.ViewModels;
using SafeAuthenticator.App.Views;
using SafeAuthenticator.Platform;

namespace SafeAuthenticator.App;

/// <summary>Avalonia application root.</summary>
public partial class App : Application
{
    private ISystemTray? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            desktop.MainWindow = mainWindow;
            SetupTray(desktop, mainWindow, mainViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ICredentialStore CreateCredentialStore()
        => OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new HeadlessCredentialStore();

    private void SetupTray(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window,
        MainWindowViewModel viewModel)
    {
        _tray = new AvaloniaSystemTray();
        _tray.Show("SafeAuthenticator", TrayCommands.DefaultMenu);

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
