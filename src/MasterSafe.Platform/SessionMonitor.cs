// File:    SessionMonitor.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Platform;

/// <summary>
/// Tells the app when the user's session is locked or the computer goes to sleep, so the vault can lock at the same
/// time. Only Windows is supported at the moment; on other systems nothing is raised.
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    /// <summary>Raised on a background thread when Windows is locked, the user is switched, or the computer suspends.</summary>
#pragma warning disable CS0067 // Only raised on Windows; other systems never raise it.
    public event EventHandler? SessionLocking;
#pragma warning restore CS0067

    /// <summary>True when this system can report these events.</summary>
    public static bool IsSupported =>
#if WINDOWS
        OperatingSystem.IsWindows();
#else
        false;
#endif

    /// <summary>Starts listening.</summary>
    public SessionMonitor()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
#endif
    }

#if WINDOWS
    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock
            or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
            or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect)
        {
            SessionLocking?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            SessionLocking?.Invoke(this, EventArgs.Empty);
        }
    }
#endif

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
#endif
    }
}
