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

            Moving over from KeePass or KeePassXC:

            - In KeePass choose File, Export, KeePass XML (2.x). In KeePassXC choose Database, Export to CSV (or XML).
            - On the Import screen choose "Choose an export file...", or drop the file onto the window.
            - A summary shows what will be added and what is skipped (recycle bin, old versions, attachments, duplicates).
              Nothing is added until you choose Import.
            - Groups become folders, one-time-password fields become authenticator accounts, and attached files
              (KeePass XML exports) come along, up to 5 MB each.
            - The export file is not encrypted. Delete it once you have imported it.

            MasterSafe validates the account details before adding them, so malformed
            or non-otpauth codes are rejected safely.
            """),
        new(
            "Passwords",
            "Store logins, copy them safely and delete several at once.",
            """
            The Passwords screen keeps logins in your encrypted vault.

            - Fill in the form and choose Save. Use Generate for a strong password.
            - Copy user and Copy password put the value on the clipboard; it is cleared after the time chosen in Settings (2 minutes at first). Copying another secret restarts the wait, and anything else you copy in the meantime is left alone.
            - Copies are kept out of the Windows clipboard history (Win+V) and the cloud clipboard, so a copied
              password does not linger there after the clipboard is cleared.
            - Search matches the title, user name, URL, folder and tags.
            - Folders: the list on the left shows your folders with how many passwords each holds. Choose one to
              see its passwords and the ones in its subfolders. Type a name and choose New folder to add one inside
              the selected folder, Rename selected to rename it, and Delete folder to remove it. Deleting a folder
              never deletes passwords: they move up to the folder above.
            - The Folder box in the form sets where an entry lives. New entries start in the folder you are viewing.
            - To move several entries, tick them, choose a folder next to "Move selected to", then choose Move.
            - Change icon gives the selected folder an icon. Pick one from the set; choose the plain folder to go back.
            - Folders are for passwords only. Authenticator accounts are kept separate.
            - Attached files: in the entry form choose "Attach a file..." (up to 5 MB each, 50 MB in the whole vault),
              then Save. Files are stored encrypted inside your vault and included in recovery backups. "Save as..."
              writes a copy to disk, and that copy is no longer protected by your vault, so delete it when done.
            - To delete several entries, tick their boxes (or choose Select all shown), then choose
              Delete selected. You are asked to confirm, and it cannot be undone.
            - Changing the search clears the ticks, so you never delete entries that are out of sight.
            """),
        new(
            "Codes and the dashboard",
            "How TOTP and HOTP codes are shown.",
            """
            The dashboard shows a code for every account.

            - TOTP codes change on a fixed schedule (usually every 30 seconds). The progress
              bar and countdown indicate when the next code appears.
            - HOTP codes advance by a counter and are shown without a countdown.
            - Use Copy code to place the current code on the clipboard. It is cleared again after the time chosen in Settings (2 minutes at first; copying another secret restarts the wait).
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

            1. Open Recovery and create a recovery key. The easiest way is "Create recovery key
               automatically": MasterSafe chooses a strong passphrase, asks where to save the key file, and
               then shows the passphrase once so you can write it down. Or type a passphrase of your own.
               Save the key file somewhere safe and away from this computer, and keep the passphrase
               written down separately.
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
