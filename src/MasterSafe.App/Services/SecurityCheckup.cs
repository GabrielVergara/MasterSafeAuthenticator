// File:    SecurityCheckup.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>One line of the settings check-up: what was looked at, whether it is fine, and what to do if not.</summary>
public sealed record CheckupItem(string Title, bool IsGood, string Detail)
{
    /// <summary>A word as well as a colour, so the result does not depend on seeing colours.</summary>
    public string Mark => IsGood ? "Good" : "Improve";
}

/// <summary>The settings the check-up looks at.</summary>
public sealed record CheckupInput(
    int AutoLockMinutes,
    bool LockOnSessionLock,
    bool BlockScreenCapture,
    int ClipboardClearSeconds,
    bool HasRecoveryKey,
    bool AutoRecoveryBackup,
    bool BreachCheckEnabled,
    int UnlockMethodCount);

/// <summary>Looks at the app's own settings and says which ones weaken protection. Nothing is changed.</summary>
public static class SecurityCheckup
{
    public static IReadOnlyList<CheckupItem> Run(CheckupInput i) =>
    [
        new("Automatic lock",
            i.AutoLockMinutes <= 10,
            i.AutoLockMinutes <= 10
                ? $"The vault locks after {i.AutoLockMinutes} minutes without use."
                : $"The vault stays open for {i.AutoLockMinutes} minutes without use. 10 minutes or less is safer. Change it in Settings."),
        new("Lock when the computer locks",
            i.LockOnSessionLock,
            i.LockOnSessionLock
                ? "The vault locks when you lock the computer."
                : "Turn on the lock when the computer is locked in Settings, so the vault is closed when you walk away."),
        new("Screen capture protection",
            i.BlockScreenCapture,
            i.BlockScreenCapture
                ? "Screenshots and screen sharing show a blank window."
                : "Turn on the screen capture block in Settings, so screenshots and screen sharing cannot show your codes."),
        new("Clipboard clearing",
            i.ClipboardClearSeconds is > 0 and <= 60,
            i.ClipboardClearSeconds is > 0 and <= 60
                ? $"Copied passwords and codes are removed from the clipboard after {i.ClipboardClearSeconds} seconds."
                : "Choose 60 seconds or less in Settings, so copied secrets do not stay on the clipboard."),
        new("Recovery key",
            i.HasRecoveryKey,
            i.HasRecoveryKey
                ? "A recovery key exists, so a forgotten master password does not lose the vault."
                : "Set up a recovery key on the Recovery page. Without one, a forgotten master password cannot be undone."),
        new("Automatic recovery backup",
            i.AutoRecoveryBackup,
            i.AutoRecoveryBackup
                ? "An encrypted recovery backup is written when the vault changes."
                : "Turn on the automatic recovery backup on the Recovery page and choose a folder that is not on this disk (for example a USB drive)."),
        new("Second way to unlock",
            i.UnlockMethodCount >= 2,
            i.UnlockMethodCount >= 2
                ? "More than one way to unlock the vault is set up."
                : "Add a second way to unlock (for example Windows Hello or a security key) in Settings, in case one is not available."),
        new("Breach check",
            i.BreachCheckEnabled,
            i.BreachCheckEnabled
                ? "Passwords can be checked against known breaches (only a short hash prefix is sent)."
                : "Optional: allow the breach check in Settings to learn whether a password was leaked. It contacts an online service, so it is your choice."),
    ];
}
