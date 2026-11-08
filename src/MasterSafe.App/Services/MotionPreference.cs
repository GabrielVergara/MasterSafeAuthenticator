// File:    MotionPreference.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.InteropServices;

namespace MasterSafe.App.Services;

/// <summary>
/// Whether the user turned animations off. On Windows that is Settings, Accessibility, Visual effects, "Animation effects".
/// Some people get dizzy or distracted by movement, so the program then shows everything still.
/// </summary>
public static class MotionPreference
{
    private const uint GetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);

    /// <summary>True when animations should not play. Replaced in tests.</summary>
    public static Func<bool> IsReduced { get; set; } = ReadSystem;

    private static bool ReadSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return SystemParametersInfo(GetClientAreaAnimation, 0, out var animate, 0) && !animate;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }
}
