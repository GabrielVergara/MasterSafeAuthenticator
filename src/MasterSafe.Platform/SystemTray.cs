// File:    SystemTray.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Platform;

/// <summary>
/// A command shown in the system tray context menu.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Label">Menu text.</param>
public sealed record TrayMenuItem(string Id, string Label);

/// <summary>
/// Cross-platform abstraction over a system tray / notification-area icon.
/// Concrete implementations (e.g. Windows) are provided by the host application.
/// </summary>
public interface ISystemTray : IDisposable
{
    /// <summary>True when a tray is available on the current OS/host.</summary>
    bool IsSupported { get; }

    /// <summary>Raised when the user activates a menu item.</summary>
    event EventHandler<string>? MenuItemClicked;

    /// <summary>Shows the tray icon with the supplied tooltip and menu.</summary>
    void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu);

    /// <summary>Requests the main window to be shown.</summary>
    void RequestShowWindow();

    /// <summary>Requests the main window to be hidden to tray.</summary>
    void RequestHideWindow();

    /// <summary>Displays a transient notification balloon.</summary>
    void Notify(string title, string message);
}

/// <summary>
/// Standard tray command identifiers used by the application.
/// </summary>
public static class TrayCommands
{
    public const string ShowWindow = "show";
    public const string HideWindow = "hide";
    public const string LockVault = "lock";
    public const string CopyRecentCode = "copy-recent";
    public const string About = "about";
    public const string Exit = "exit";

    /// <summary>The default tray menu layout.</summary>
    public static IReadOnlyList<TrayMenuItem> DefaultMenu { get; } =
    [
        new(ShowWindow, "Show window"),
        new(HideWindow, "Hide window"),
        new(LockVault, "Lock vault"),
        new(CopyRecentCode, "Copy recent code"),
        new(About, "About"),
        new(Exit, "Exit"),
    ];
}

/// <summary>
/// No-op tray used on platforms/hosts without a notification area. Keeps the app functional
/// everywhere without faking OS integration.
/// </summary>
public sealed class HeadlessSystemTray : ISystemTray
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public event EventHandler<string>? MenuItemClicked;

    /// <inheritdoc />
    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
    }

    /// <inheritdoc />
    public void RequestShowWindow() => MenuItemClicked?.Invoke(this, TrayCommands.ShowWindow);

    /// <inheritdoc />
    public void RequestHideWindow() => MenuItemClicked?.Invoke(this, TrayCommands.HideWindow);

    /// <inheritdoc />
    public void Notify(string title, string message)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
