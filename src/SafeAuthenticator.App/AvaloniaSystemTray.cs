// File:    AvaloniaSystemTray.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using SafeAuthenticator.Platform;

namespace SafeAuthenticator.App;

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

        Dispatcher.UIThread.Invoke(() =>
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
                Menu = nativeMenu,
                IsVisible = true,
            };

            _trayIcon.Clicked += (_, _) => MenuItemClicked?.Invoke(this, TrayCommands.ShowWindow);
        });
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
        Dispatcher.UIThread.Invoke(() =>
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
        Dispatcher.UIThread.Invoke(() =>
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        });
    }
}
