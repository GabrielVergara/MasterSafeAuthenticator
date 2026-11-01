// File:    GlobalLockHotkey.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace MasterSafe.App.Services;

/// <summary>
/// Ctrl+Alt+L locks the vault from any program, also while the window is hidden in the tray. Windows only. Off until
/// the user turns it on, because a system-wide shortcut can clash with another program.
/// </summary>
public sealed class GlobalLockHotkey(Window window, Action pressed) : IDisposable
{
    private const int HotkeyId = 0x4D53;
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint KeyL = 0x4C;

    private IntPtr _registeredOn;
    private bool _hooked;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>Turns the shortcut on or off. Returns false when it could not be turned on (another program has it, or not Windows).</summary>
    public bool Apply(bool enabled)
    {
        if (!enabled)
        {
            Release();
            return true;
        }

        if (_registeredOn != IntPtr.Zero)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle)
        {
            return false;
        }

        if (!_hooked)
        {
            Win32Properties.AddWndProcHookCallback(window, OnMessage);
            _hooked = true;
        }

        if (!RegisterHotKey(handle.Handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, KeyL))
        {
            return false;
        }

        _registeredOn = handle.Handle;
        return true;
    }

    private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            pressed();
        }

        return IntPtr.Zero;
    }

    private void Release()
    {
        if (_registeredOn != IntPtr.Zero)
        {
            UnregisterHotKey(_registeredOn, HotkeyId);
            _registeredOn = IntPtr.Zero;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Release();
}
