// File:    WindowCaptureProtection.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace MasterSafe.App.Services;

/// <summary>
/// Keeps the window out of screenshots, screen recordings and screen sharing (Windows 10 version 2004 and later).
/// The window then shows as a black or missing area in the capture, so passwords and codes on screen cannot leak that way.
/// </summary>
public static class WindowCaptureProtection
{
    private const uint None = 0x0;
    private const uint ExcludeFromCapture = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    /// <summary>Turns the protection on or off. Returns false when this system or window does not support it.</summary>
    public static bool Apply(Window window, bool block)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return SetWindowDisplayAffinity(handle, block ? ExcludeFromCapture : None);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }
}
