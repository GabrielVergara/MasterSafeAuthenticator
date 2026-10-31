// File:    AvaloniaSystemTray.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using MasterSafe.Platform;

namespace MasterSafe.App;

/// <summary>
/// An <see cref="ISystemTray"/> implementation backed by Avalonia's cross-platform
/// <see cref="TrayIcon"/>. Available on desktop platforms where Avalonia exposes a
/// notification-area icon (Windows, and most Linux/macOS desktops).
/// </summary>
public sealed class AvaloniaSystemTray : ISystemTray
{
    private TrayIcon? _trayIcon;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public event EventHandler<string>? MenuItemClicked;

    /// <inheritdoc />
    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        OnUiThread(() =>
        {
            _trayIcon?.Dispose();

            var nativeMenu = new NativeMenu();
            foreach (var item in menu)
            {
                var id = item.Id;
                var menuItem = new NativeMenuItem(item.Label);
                menuItem.Click += (_, _) => MenuItemClicked?.Invoke(this, id);
                nativeMenu.Add(menuItem);
            }

            _trayIcon = new TrayIcon
            {
                ToolTipText = tooltip,
                Icon = LoadIcon(),
                Menu = nativeMenu,
                IsVisible = true,
            };

            _trayIcon.Clicked += (_, _) => MenuItemClicked?.Invoke(this, TrayCommands.ShowWindow);
        });
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://MasterSafe.App/Assets/tray-32.png")));
        }
        catch (Exception)
        {
            // The tray still works with the platform default icon.
            return null;
        }
    }

    /// <inheritdoc />
    public void RequestShowWindow() => MenuItemClicked?.Invoke(this, TrayCommands.ShowWindow);

    /// <inheritdoc />
    public void RequestHideWindow() => MenuItemClicked?.Invoke(this, TrayCommands.HideWindow);

    /// <inheritdoc />
    public void Notify(string title, string message)
    {
        // Avalonia's TrayIcon does not expose balloon notifications in a cross-platform way;
        // update the tooltip so the user still gets feedback where the tray is visible.
        OnUiThread(() =>
        {
            if (_trayIcon is not null)
            {
                _trayIcon.ToolTipText = $"{title}: {message}";
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnUiThread(() =>
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        });
    }

    // Tray icons must be changed on the UI thread. When there is no running application (tests, or after shutdown)
    // there is no UI thread to wait for, so the action runs directly instead of blocking forever.
    private static void OnUiThread(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }
}
