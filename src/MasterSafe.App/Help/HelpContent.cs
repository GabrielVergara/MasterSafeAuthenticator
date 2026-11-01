// File:    HelpContent.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
    private static readonly HelpTopic[] Source =
    [
        new(
            "Welcome and first steps",
            "Create your vault and set it up in a few minutes.",
            """
            MasterSafe Authenticator keeps your one-time-password accounts (the 6-digit codes of
            two-factor sign-in) and your passwords in one encrypted vault on your computer. Nothing is
            sent anywhere unless you choose a feature that needs the network, and each of those is
            described in this help.

            First steps:

            1. Create the vault. Choose a master password: a long phrase of several words is both
               strong and easy to remember. MasterSafe refuses passwords that are very easy to guess.
               It cannot be recovered if you forget it, which is why the next step matters.
            2. Set up a recovery key (Recovery). The button "Create recovery key automatically" does
               it in one step. See "Recovery and the forgotten master password".
            3. Add Windows Hello or a security key in Settings if you want quicker sign-in. Your master
               password always keeps working.
            4. Add your accounts and passwords: type them, drop a QR code image on the window, or
               import them from KeePass. See "Adding and importing".

            The codes page shows reminders for the steps you have not done yet. Each one has a
            "Not now" button, and you can switch them all off in Settings.
            """),
        new(
            "Authenticator codes",
            "Read, copy, search, order and rename your codes.",
            """
            The Authenticator codes page lists every account with its current code.

            - TOTP codes change on a fixed schedule, usually every 30 seconds. The bar and the countdown
              show when the next one arrives. HOTP codes advance by a counter and have no countdown.
            - Copy code puts the code on the clipboard. It is removed again after the time you chose in
              Settings (2 minutes at first), and copying another secret restarts that wait.
            - With more than three accounts a search box and an order box appear. Search matches the issuer
              (for example GitHub) and the label (usually your user name). The order is remembered.
            - Rename changes the issuer and the label only. The secret and the codes stay the same.
            - Delete removes an account at once after a confirmation. Make sure you can still sign in to that
              service another way first: without the secret there is no way to get the codes back, except
              from a backup.
            """),
        new(
            "Adding and importing",
            "Types of links, QR images and KeePass exports.",
            """
            Adding one account:
            - Add account: type the issuer, the label and the secret key shown by the service.
            - Import: paste the otpauth:// link that sits behind a QR code (for example
              otpauth://totp/Issuer:user?secret=...).
            - QR code image: drag a PNG, JPG or BMP picture of the QR code onto the window while the vault is
              unlocked, or choose it with "Choose an image...". Several images can be dropped at once. An account
              that is already in your vault is not added twice.

            Moving over from KeePass or KeePassXC:
            - In KeePass choose File, Export, KeePass XML (2.x). In KeePassXC choose Database, Export to CSV
              (or XML). Then use "Choose an export file..." on the Import page, or drop the file on the window.
            - A summary shows what will be added and what is skipped (recycle bin entries, old versions,
              duplicates, files that are too large). Nothing is added until you choose Import.
            - Groups become folders, one-time-password fields become authenticator accounts, and attached files
              (KeePass XML exports) come along, up to 5 MB each.
            - The export file is not encrypted. Delete it as soon as you have imported it. The page has a
              "Show file" button for that. Opening .kdbx files directly is not supported yet.

            MasterSafe checks every account and file it reads. Broken or unexpected files are refused with a
            message instead of being half imported.
            """),
        new(
            "Passwords",
            "Store logins, make strong ones and copy them safely.",
            """
            The Passwords page keeps logins in your encrypted vault.

            - Fill in the form and choose Save. "Generate strong password" makes a random one.
            - The strength bar under the password field estimates how hard it is to guess. It is an
              estimate: it cannot see every weak pattern, so a long random password or a phrase of several
              unrelated words is always the safer choice.
            - Copy user and Copy password put the value on the clipboard. It is removed again after the time
              set in Settings, and it is kept out of the Windows clipboard history (Win+V) and the cloud
              clipboard, so it does not linger there.
            - Open site opens the address of the entry in your browser. Only http and https addresses are
              opened, and an unencrypted http address comes with a warning.
            - Search matches the title, user name, address, folder and tags. The order box sorts by title,
              by recently changed, or by folder.
            - Tick several entries to delete or move them together. Changing the search or the folder
              clears the ticks, so something you cannot see is never deleted by accident.
            """),
        new(
            "Folders and icons",
            "Organise passwords in folders, with icons you choose.",
            """
            The list on the left of the Passwords page shows your folders and how many passwords each one
            holds. Choose a folder to see its passwords and those in its subfolders.

            - New folder: type a name in the box under the list and choose New folder. It is created inside
              the folder that is selected. A name like Work/Remote makes both levels.
            - Rename selected renames a folder with everything inside it. Delete folder removes the folder
              only: its passwords move up one level and are never deleted.
            - Change icon gives a folder one of 24 icons. Choose the plain folder to go back.
            - Move passwords: set the Folder in the entry form, tick several entries and use "Move selected
              to", or drag an entry onto a folder. Dragging a ticked entry moves all ticked entries.
            - Right-click a folder for the same actions. Drag the divider to change the width of the
              list, and double-click the divider to fit it to the folder names again.

            Folders are for passwords only. Authenticator accounts are kept separate.
            """),
        new(
            "Attached files",
            "Keep documents with a password entry.",
            """
            An entry can carry files, for example a recovery-codes text file or a licence key.

            - In the entry form choose "Attach a file..." and then Save. Each file can be up to 5 MB and
              the whole vault can hold 50 MB of attachments.
            - The files are stored encrypted inside your vault, together with the passwords, and they are
              part of recovery backups. A vault without attachments keeps its previous file layout.
            - "Save as..." writes a copy to a place you choose. That copy is an ordinary file and is no
              longer protected by your vault, so delete it when you are done.
            - Names are cleaned up: a file can never be saved outside the folder you chose.
            """),
        new(
            "Recycle bin and history",
            "Undo a delete, and find an old password.",
            """
            Recycle bin
            - Deleting a password moves it to the Recycle bin at the end of the folder list, with its
              attached files. It stays there for 30 days and can be restored to its folder.
            - Delete for good, or Empty the recycle bin, removes entries immediately. That cannot be undone.
            - After 30 days the app removes entries from the bin by itself when it opens the vault.

            Earlier versions
            - When you change the title, user name, password, address or notes of an entry, the old values are
              kept as an earlier version, up to ten per entry. Open the entry with Edit to see them.
            - "Use this version" puts the old values back into the form (they are only kept when you choose
              Save), and "Copy its password" is handy when a site still expects the old one.

            Both are stored inside the encrypted vault. Old passwords therefore stay in the vault until
            their entry is deleted for good.
            """),
        new(
            "Password health",
            "Find weak, reused and old passwords.",
            """
            The top of the page is a Security check-up of MasterSafe itself: the lock time, locking with the
            computer, the screen capture block, clipboard clearing, the recovery key and backup, a second way
            to unlock and the breach check. Each line says "Good" or "Improve" and what to change. Nothing is
            changed for you.

            Below it, the page checks your passwords on this computer:

            - Weak: easy to guess because they are short, simple, very common, or built from the title or
              user name.
            - Reused: the same password is used by more than one entry. If one site leaks it, the others
              are at risk too.
            - Old: not changed for more than a year.

            Each line has an Open button that takes you to the entry.

            Optional breach check
            It can also look your passwords up in known data breaches using the Pwned Passwords service of
            Have I Been Pwned (haveibeenpwned.com, by Troy Hunt). It is off until you allow it in Settings.
            When you run it, your password is never sent: MasterSafe computes its SHA-1 hash on your computer
            and sends only the first 5 characters to api.pwnedpasswords.com. The service answers with all
            hashes that start the same way, and the comparison happens here. A password found in a breach
            should be changed whatever its strength.
            """),
        new(
            "Keyboard and mouse",
            "Shortcuts and quick actions.",
            """
            On the Passwords page the shortcuts act on the current entry: the one you last clicked or edited,
            or the only one shown.

            - Ctrl+F: search
            - Ctrl+N: new entry
            - Ctrl+B: copy the user name
            - Ctrl+C: copy the password (inside a text box it copies the selected text as usual)
            - Ctrl+U: open the site
            - Delete: move the entry to the recycle bin (after a confirmation)

            With the mouse:
            - Double-click an entry to copy its password.
            - Right-click an entry or a folder for a menu.
            - Drag an entry onto a folder to move it, or onto the Recycle bin to delete it.
            - Drop a QR code image anywhere on the window to import an account.
            """),
        new(
            "Accessibility",
            "Screen readers, keyboard, text size and contrast.",
            """
            MasterSafe is built to be usable without a mouse and with assistive technology.

            - Screen readers (Narrator, NVDA): every control has a name, page titles are headings, and
              messages such as "Copied" are read out. Codes are read when you ask for them, not every second.
            - Keyboard: press Alt and the underlined letter to go to a page, F1 for this help, Ctrl+L to lock,
              Tab and Shift+Tab to move, Enter or Space to press a button. Everything that can be done by
              dragging can also be done with a button.
            - Text and window size: Settings, "Text and window size" (100 to 200%), or press Ctrl and plus or
              minus at any time. Ctrl and 0 returns to normal.
            - Contrast: Settings, Theme, "High contrast". With "Follow the system", Windows high contrast mode
              switches it on by itself.
            - Time: a warning appears 30 seconds before the vault locks by itself. Choose "Keep unlocked" or
              press any key. You can change the time in Settings.
            - Language: Settings, Language (English, Spanish, Finnish).
            - Reading: Settings, "Easier reading" adds space between letters, words and lines.

            If something is hard to use with your tools, please tell us: see "About, licence and support".
            """),
        new(
            "Backups",
            "Encrypted copies of your accounts.",
            """
            The Backup screen writes an encrypted .sauth file with your authenticator accounts. Only your
            master password can open it. No plaintext export is ever produced.

            Destinations:
            - Local Folder: choose a folder on this device.
            - WebDAV Cloud: enter the folder address (it must start with https://) plus user name and
              password for a WebDAV server such as Nextcloud or ownCloud. Use an app password if your server
              offers one.

            Use Export to create a backup, Refresh to list existing backups, and Import selected to restore
            one. Backups are encrypted with Argon2id and AES-256-GCM.

            This backup holds accounts only. To protect your passwords and attached files as well, use
            recovery backups, described in the next topic.
            """),
        new(
            "Recovery and the forgotten master password",
            "Recovery key, recovery backups and starting over.",
            """
            Nobody can reset a master password, because your data is encrypted with it. Prepare while you
            still remember it.

            1. Open Recovery and create a recovery key. The easiest way is "Create recovery key
               automatically": MasterSafe chooses a strong passphrase, asks where to save the key file, and
               then shows the passphrase once so you can write it down. Or type a passphrase of your own.
               Save the key file somewhere safe and away from this computer, and keep the passphrase written
               down separately.
            2. Create a recovery backup. It holds your accounts, passwords, folders and attached files, locked
               so that only your recovery key can open it. Keep it anywhere, even in the cloud.
            3. Optional: switch on "Save recovery backups automatically" and choose a folder. A minute after
               you change something, MasterSafe writes a new backup there and keeps the newest five. It never
               writes one while the vault is locked or empty.

            Recovery backups use a hybrid scheme that combines a classical key exchange with a post-quantum
            one (ML-KEM-768), so they stay protected even against future quantum computers.

            If you forget the password:
            - On the sign-in screen choose "Forgot your master password?".
            - Restore from a recovery backup: pick the backup file, your key file, its passphrase and a new
              master password.
            - Or start a new vault. Your old vault is moved to the archive, not deleted. If you find the
              password later, open the old vault from the Vaults page.
            - Windows Hello or a security key may still open your vault. If so, change the master password in
              Settings.
            """),
        new(
            "Unlock methods",
            "Windows Hello and security keys.",
            """
            Besides the master password you can unlock with:

            - Windows Hello: uses a key held by Windows (in the TPM when your computer has one) that only works
              after you verify with your PIN, fingerprint or face.
            - A FIDO2 security key: the key computes a secret that never leaves it. It needs the key's PIN
              and a touch. Most current keys support the hmac-secret feature this needs. One security key can
              be set up per vault.

            Add or remove them in Settings. The master password always keeps working and cannot be removed.
            Wrong attempts with Windows Hello or a key are limited by Windows and by the key itself.
            """),
        new(
            "Privacy and locking",
            "How MasterSafe keeps secrets from leaking.",
            """
            Settings, "Privacy and locking" and General:

            - Sign-in pause: after 5 wrong master passwords in a row, signing in pauses for 30 seconds, then
              1, 2, 5 and finally 15 minutes with each further mistake. A correct password resets it, and the
              next unlock tells you how many wrong passwords were entered. The counter survives restarting
              the app. This stops guessing at your keyboard. It cannot stop someone who copies the vault file
              and guesses elsewhere: that is what the slow Argon2id key derivation and a long master
              password are for. Nothing is ever deleted because of wrong passwords.
            - Automatic lock: the vault locks after the minutes of inactivity you set (5 at first).
            - Quick lock: Ctrl+L inside the app, "Lock vault" in the tray menu, and optionally Ctrl+Alt+L from
              any program (Windows, off by default).
            - Lock when Windows locks or sleeps (on by default) and, optionally, when the window is
              minimized.
            - Keep the window out of screenshots and screen sharing (on by default, Windows): the window shows
              as black in captures. Turn it off if you need to take a screenshot of MasterSafe.
            - Clipboard: choose 30 seconds, 1 minute or 2 minutes. Copies are kept out of the Windows
              clipboard history and the cloud clipboard.
            - Locking removes the decrypted data from memory-backed screens and wipes the session key.
            """),
        new(
            "Settings and the tray",
            "Themes, reminders and the system tray.",
            """
            - Theme: System, Light, Dark or High contrast, applied immediately.
            - Language: English, Spanish or Finnish. It changes completely when MasterSafe is restarted.
            - Strength of the password protection: "Test and strengthen" measures this computer and makes
              guessing your master password as slow as it can be while unlocking still takes about a second.
              Type your master password to confirm. Nothing else changes.
            - Reminders: show or hide the notices on the codes page (recovery key, backups, updates).
            - Keep running in the tray: closing or minimizing the window hides it to the notification area
              instead of exiting. Open it again by clicking the tray icon. Its menu shows or hides the window,
              locks the vault, opens About, or exits.
            - Exit: the Exit button in the sidebar and on the sign-in screen locks the vault and closes MasterSafe
              completely, whatever the tray setting.
            - Change master password: re-protects your vault with a new password. Windows Hello and a security
              key keep working.

            On Windows 11, a new tray icon starts in the hidden-icons area (the arrow next to the clock). To
            keep it always visible, drag it out onto the taskbar, or turn it on under Settings,
            Personalization, Taskbar, Other system tray icons.
            """),
        new(
            "Updates",
            "Get new versions safely.",
            """
            Settings has an Updates section.

            - "Check for updates" asks GitHub for the newest release. Optionally, MasterSafe can do this once
              after you unlock. That is off by default, because it contacts github.com.
            - "Download and verify" saves the installer for your system and checks its ML-DSA-65 signature and
              SHA-256 checksum against the project's signing key, which is built into the app. A download that
              does not verify is deleted. You then close MasterSafe and run the installer yourself. Updates
              are never installed automatically, and your vault is not touched.
            - Nothing about you or your vault is sent. Only the public release information is requested.

            You can also verify any download by hand with the MasterSafe command-line tool. The key's
            fingerprint is published in the README and in SECURITY.md of the project.
            """),
        new(
            "Vaults and files",
            "Where your data is stored and how to open another vault.",
            """
            Your data is one encrypted file, a vault, kept in the MasterSafe folder.
            Windows: %LocalAppData%\MasterSafe. Linux: ~/.local/share/MasterSafe (readable by you only).

            The Vaults page shows these locations and lists your vaults:
            - Open a vault file: use a vault from a USB stick or another folder. MasterSafe leaves the file
              where it is.
            - Start a new vault: moves the current vault into the archive and creates an empty one.
            - Open an archived vault again, show a file in its folder, or delete an old vault. Deleting asks
              for confirmation and cannot be undone.

            To move to another computer, copy vault.sauth there, install MasterSafe and choose "I already have
            a vault file". Uninstalling never removes your vault. Saving is crash-safe, and the previous
            version of the vault is kept as vault.sauth.bak, with daily copies in the snapshots folder. Changing the
            master password or removing an unlock method deletes those copies, because the old way in still opens them.
            """),
        new(
            "Security model",
            "How your data is protected, and the limits.",
            """
            MasterSafe is built security-first.

            - Your master password is stretched with Argon2id (128 MiB of memory by default, up to 512 MiB tuned to
              your computer) into a 256-bit key.
            - The vault, including attached files, is compressed and encrypted with AES-256-GCM, which also
              authenticates the data to detect tampering.
            - The vault is encrypted with one random key. That key is stored once for each way of unlocking
              (master password, Windows Hello, security key), each protected separately.
            - Releases are signed with the post-quantum signature ML-DSA-65, so a download can be proven
              genuine. Recovery backups use a hybrid ML-KEM-768 plus ECDH P-384 scheme.
            - Files you open (vaults, backups, KeePass exports, QR images, downloads) are treated as untrusted:
              sizes, key-derivation settings, nesting and counts are limited before anything is read.
            - Secrets are never written to disk in plaintext, never logged, and not sent over the network.

            Limits you should know about
            - While the vault is unlocked, its contents are in memory. Malware running as you can read them.
              Keep your computer updated and use the automatic lock.
            - A copy saved with "Save as..." or an export from another program is not protected.
            - No software can promise to be unbreakable. A long, unique master password matters most.
            """),
        new(
            "Troubleshooting",
            "Common questions and what to do.",
            """
            I forgot my master password.
            See "Recovery and the forgotten master password". Without a recovery key or an unlock method that
            still works, the data cannot be opened.

            Sign-in says "Too many wrong passwords".
            Wait for the countdown to finish. It protects against guessing. A correct password afterwards works
            normally.

            A code is not accepted.
            Codes depend on the clock. Make sure your computer's time and time zone are correct, then try the
            next code.

            The window is black in my screenshot.
            That is the setting that keeps MasterSafe out of screenshots. Turn it off in Settings, Privacy and
            locking, while you capture.

            Copying a password does nothing.
            Another program may be holding the clipboard. Try again. If it keeps failing, close clipboard
            managers.

            The tray icon is missing.
            On Windows 11 look under the arrow next to the clock and drag the icon onto the taskbar.

            The installer shows a SmartScreen warning.
            The installer is not signed with a paid Authenticode certificate yet. Check the download with
            "Download and verify" in a later update, or with the command-line tool, and compare the key
            fingerprint with the one in the project's README.
            """),
        new(
            "About, licence and support",
            "Who makes MasterSafe and where to read more.",
            """
            MasterSafe Authenticator is written by Gabriel Vergara Ezcurdia. It is free for personal,
            non-commercial use; see the LICENSE file for the terms. Its third-party components and their
            licences are listed in THIRD-PARTY-NOTICES.md.

            - Article about MasterSafe: https://www.gabriel-vergara.com/articles/mastersafe-authenticator/
            - Website: https://www.gabriel-vergara.com/
            - Source code, releases and issues: https://github.com/GabrielVergara/MasterSafeAuthenticator

            The About window has buttons for all of these.

            To report a security problem, follow SECURITY.md in the project and do not post it publicly.
            """),
    ];

    /// <summary>All help topics, in display order, with each paragraph joined into one flowing line.</summary>
    public static IReadOnlyList<HelpTopic> Topics { get; } = Source.Select(t => t with { Body = Reflow(t.Body) }).ToArray();

    /// <summary>
    /// The help text is written with line breaks inside paragraphs so that the source is easy to read. This joins a line
    /// with the one before it when that one was clearly wrapped (it is long and not a heading or a list item), so the
    /// text wraps to whatever width the window has. Blank lines, headings and list items stay where they are.
    /// </summary>
    public static string Reflow(string text)
    {
        var output = new List<string>();
        string? current = null;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (current is not null)
                {
                    output.Add(current);
                    current = null;
                }

                output.Add(string.Empty);
                continue;
            }

            var startsItem = Regex.IsMatch(line.TrimStart(), @"^(-\s|\d+\.\s)");
            var previousWasWrapped = current is { Length: >= 60 };
            if (current is not null && !startsItem && previousWasWrapped)
            {
                current += " " + line.Trim();
            }
            else
            {
                if (current is not null)
                {
                    output.Add(current);
                }

                current = line;
            }
        }

        if (current is not null)
        {
            output.Add(current);
        }

        return string.Join("\n", output);
    }
}
