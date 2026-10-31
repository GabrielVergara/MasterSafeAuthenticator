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

            1. The first time, choose a master password and create your vault. Use a long
               passphrase. It cannot be recovered if you forget it.
            2. Later, enter it on the login screen and choose Unlock, or use Windows Hello or
               your security key if you have set them up in Settings.
            3. The dashboard lists each account with a live 6-digit code.
            4. Each code refreshes automatically; the countdown shows the time remaining.

            Your secrets never leave the device in plaintext and the vault is only decrypted
            in memory while the app is unlocked.
            """),
        new(
            "Adding accounts",
            "Import accounts from an otpauth URI or a QR image.",
            """
            Open the Import screen to add an account.

            - Paste an otpauth:// URI (for example otpauth://totp/Issuer:user?secret=...).
            - Or drag a QR image (png, jpg, jpeg, or bmp) onto the window while the vault is unlocked,
              or use "Choose an image..." on the Import screen. You can drop several images at once.

            An account that is already in your vault is not added a second time.

            MasterSafe validates the account details before adding them, so malformed
            or non-otpauth codes are rejected safely.
            """),
        new(
            "Codes and the dashboard",
            "How TOTP and HOTP codes are shown.",
            """
            The dashboard shows a code for every account.

            - TOTP codes change on a fixed schedule (usually every 30 seconds). The progress
              bar and countdown indicate when the next code appears.
            - HOTP codes advance by a counter and are shown without a countdown.
            - Use Copy code to place the current code on the clipboard.
            - Delete removes an account from the vault immediately. Make sure you can still
              sign in to the service some other way first.

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
            - WebDAV Cloud: enter the folder address (it must start with https://) plus user
              name and password for a WebDAV server such as Nextcloud or ownCloud. Use an app
              password if your server offers one.

            Use Export to create a backup, Refresh to list existing backups, and Import
            selected to restore one. Backups are encrypted with Argon2id and AES-256-GCM.
            """),
        new(
            "Settings and the tray",
            "Themes, auto-lock, and the system tray.",
            """
            The Settings screen controls appearance and behavior.

            - Theme: System, Light, or Dark, applied immediately.
            - Keep running in the tray: closing or minimizing the window hides it to the notification
              area instead of exiting. Open it again by clicking the tray icon, and use its menu
              to show the window, open About or exit.
            - Lock the vault after: the number of minutes without keyboard or mouse use before
              the vault locks. Locking wipes the decrypted data from memory.
            - Unlock methods: add or remove Windows Hello or a FIDO2 security key. Your master
              password always keeps working, and it cannot be removed. A security key needs
              the hmac-secret feature (most current keys have it) and asks for its PIN and a
              touch. One security key can be set up per vault.
            - Change master password: re-protects your vault with a new password. Windows
              Hello keeps working.

            On Windows 11, a new tray icon starts in the hidden-icons area (the arrow next to the
            clock). To keep it always visible, drag it out onto the taskbar, or turn it on under
            Settings, Personalization, Taskbar, Other system tray icons.
            """),
        new(
            "Forgotten master password",
            "Recovery keys, recovery backups and starting over.",
            """
            Nobody can reset a master password, because your data is encrypted with it. Prepare while you
            still remember it:

            1. Open Recovery and create a recovery key. Save the key file somewhere safe and away from
               this computer, and write down its passphrase separately.
            2. Create a recovery backup there now and then, especially after adding accounts or passwords.
               It holds your accounts and passwords, locked so that only your recovery key can open it.

            If you forget the password:

            - On the sign-in screen choose "Forgot your master password?".
            - Restore from a recovery backup: pick the backup file, your key file, its passphrase and a
              new master password.
            - Or start a new vault. Your old vault is moved to the archive, not deleted. If you find the
              password later, open the old vault from the Vaults page.
            - Windows Hello or a security key may still open your vault. If so, change the master
              password in Settings.
            """),
        new(
            "Vaults and files",
            "Where your data is stored and how to open another vault.",
            """
            Your data is one encrypted file, a vault, kept in the MasterSafe folder:
            Windows: %LocalAppData%\MasterSafe. Linux: ~/.local/share/MasterSafe.

            The Vaults page shows these locations and lists your vaults:
            - Open a vault file: use a vault from a USB stick or another folder. MasterSafe leaves the file
              where it is.
            - Start a new vault: moves the current vault into the archive and creates an empty one.
            - Open an archived vault again, show a file in its folder, or delete an old vault. Deleting asks
              for confirmation and cannot be undone.

            To move to another computer, copy vault.sauth there, install MasterSafe and choose "I already
            have a vault file". Uninstalling never removes your vault.
            """),
        new(
            "Security model",
            "How your data is protected.",
            """
            MasterSafe is built security-first.

            - Your master password is stretched with Argon2id into a 256-bit key.
            - Account records are compressed and encrypted with AES-256-GCM, which also
              authenticates the data to detect tampering.
            - The vault is encrypted with one random key. That key is stored once for each way
              of unlocking (master password, Windows Hello), each protected separately.
            - Windows Hello uses a key held by Windows (the TPM when available) that only
              works after you verify with your PIN or biometrics.
            - A security key computes a secret inside the key that never leaves it, and needs
              the key's PIN and a touch. Without the physical key the vault cannot be opened
              this way.
            - Saving is crash-safe, and the previous version is kept as vault.sauth.bak.
            - Backups sent to WebDAV travel over https and are already encrypted.

            Secrets are never written to disk in plaintext. No software can promise to be
            unbreakable, so keep your computer updated and your master password long.
            """),
    ];
}
