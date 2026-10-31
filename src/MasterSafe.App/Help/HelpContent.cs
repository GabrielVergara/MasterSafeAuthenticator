// File:    HelpContent.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.Generic;

namespace MasterSafe.App.Help;

/// <summary>A single help article shown in the Help page.</summary>
/// <param name="Title">Short title shown in the topic list.</param>
/// <param name="Summary">One-line description.</param>
/// <param name="Body">Full help text (plain text with blank-line paragraphs).</param>
public sealed record HelpTopic(string Title, string Summary, string Body);

/// <summary>
/// Supplies the built-in help content. Pages are generated in code so they ship with the
/// application and work fully offline.
/// </summary>
public static class HelpContent
{
    /// <summary>All help topics, in display order.</summary>
    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new(
            "Getting started",
            "Unlock the vault and read your first code.",
            """
            MasterSafe keeps your two-factor accounts in an encrypted vault that is
            protected by your master password.

            1. Enter your master password on the login screen and choose Unlock.
            2. The dashboard lists each account with a live 6-digit code.
            3. Each code refreshes automatically; the ring shows the time remaining.

            Your secrets never leave the device in plaintext and the vault is only decrypted
            in memory while the app is unlocked.
            """),
        new(
            "Adding accounts",
            "Import accounts from an otpauth URI or a QR image.",
            """
            Open the Import screen to add an account.

            - Paste an otpauth:// URI (for example otpauth://totp/Issuer:user?secret=...).
            - Or drag a QR image (png, jpg, jpeg, or bmp) onto the window to decode it.

            MasterSafe validates the account details before adding them, so malformed
            or non-otpauth codes are rejected safely.
            """),
        new(
            "Codes and the dashboard",
            "How TOTP and HOTP codes are shown.",
            """
            The dashboard shows a code for every account.

            - TOTP codes change on a fixed schedule (usually every 30 seconds). The progress
              ring and countdown indicate when the next code appears.
            - HOTP codes advance by a counter and are shown without a countdown.
            - Use the Copy button to place the current code on your status line for quick entry.

            Supported algorithms include SHA-1, SHA-256, and SHA-512 with 6 or 8 digits.
            """),
        new(
            "Backups",
            "Export and restore an encrypted .sauth backup.",
            """
            The Backup screen writes an encrypted .sauth file that only your master password
            can open. No plaintext export is ever produced.

            Destinations:
            - Local Folder: choose a folder on this device.
            - WebDAV Cloud: enter a collection URL plus username and password for a WebDAV
              server such as Nextcloud or ownCloud.

            Use Export to create a backup, Refresh to list existing backups, and Import
            selected to restore one. Backups are encrypted with Argon2id and AES-256-GCM.
            """),
        new(
            "Settings and the tray",
            "Themes, auto-lock, and the system tray.",
            """
            The Settings screen controls appearance and behavior.

            - Theme: System, Light, or Dark, applied immediately.
            - Minimize to tray: closing the window hides it to the notification area instead
              of exiting.
            - Auto-lock after: the number of minutes of inactivity before the vault locks.

            The tray icon menu lets you show or hide the window, copy the most recent code,
            open About, and exit the application.
            """),
        new(
            "Security model",
            "How your data is protected.",
            """
            MasterSafe is built security-first.

            - Your master password is stretched with Argon2id into a 256-bit key.
            - Account records are compressed and encrypted with AES-256-GCM, which also
              authenticates the data to detect tampering.
            - On Windows, an optional DPAPI layer adds defense in depth.
            - A post-quantum abstraction is included and refuses to fake cryptography when a
              real provider is unavailable.

            Secrets are never written to disk in plaintext.
            """),
    ];
}
