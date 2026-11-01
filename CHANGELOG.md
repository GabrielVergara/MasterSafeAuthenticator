# Changelog

All notable changes to MasterSafe Authenticator. The newest version is first. Dates are when the version was built.

## Unreleased

- The article link in About, Help and the README now opens the live site, not the test copy.
- Documentation: the roadmap, threat model, OWASP checklist and signing notes now match 1.5.0.

## 1.5.0 (2026-11-01)

### Accessibility
- **Screen readers:** every input, button, list and progress bar now has an accessible name, headings carry their level, status messages are announced, and the automatic-lock warning is announced at once. A test opens every page and fails if a control has no name.
- **Text and window size** 100 to 200% in Settings, and Ctrl and plus / minus / 0 anywhere. The layout reflows.
- **High contrast theme**, also chosen automatically when Windows high contrast is on and the theme is "Follow the system".
- **Keyboard:** access keys on the navigation (Alt and the underlined letter), F1 for help, Ctrl+L to lock.
- **Time:** a warning with "Keep unlocked" 30 seconds before the automatic lock.
- **Easier reading** in Settings: more space between letters, words and lines.
- New [accessibility statement](docs/Accessibility.md).

### Getting started
- **Exit button** in the sidebar and on the sign-in screen: locks the vault and closes the program, whatever the tray setting.
- **First-run guide:** after a new vault is created, one page lists the steps that matter (recovery key, quicker unlock, first account, passwords), shows what is done and goes to the right page. Every step can be skipped.

### Languages
- **Spanish (Español) and Finnish (Suomi)** besides English: every screen, the Help, the tray menu and the app's messages. System error texts stay in the language they come in. The language follows the computer and can be chosen in Settings; a change applies fully after a restart. See [docs/Translating.md](docs/Translating.md) for how texts are translated and how to add a language. A test fails when a text on a screen has no translation.

### Security
- **Security check-up** at the top of the Health page: looks at the lock time, lock on computer lock, screen capture block, clipboard clearing, recovery key, recovery backup, second unlock method and breach check, and says what to change. Nothing is changed for you.
- **Test and strengthen** in Settings: measures this computer and moves the vault to the strongest Argon2id setting (up to 512 MiB) that still unlocks in about a second. The master password does not change; the page also shows the current setting.
- **Quick lock:** "Lock vault" in the tray menu and, if you turn it on in Settings, Ctrl+Alt+L from any program (Windows).
- **Revealed passwords hide again** by themselves after 20 seconds.
- **Review fixes:**
  - Changing the master password, strengthening the protection or removing an unlock method now removes `vault.sauth.bak` and the daily snapshots. Those copies still opened with the old password and hold the same data key as the current vault.
  - Password boxes no longer give their plain text to accessibility tools; they report dots, so a screen reader cannot speak a master password.
  - Moving a vault to the archive removes its snapshots, so "delete forever" really is the end of it.
  - The password typed for "Test and strengthen" is cleared when the vault locks or the step fails.
  - Translation no longer touches your own text (an entry called "Password" keeps that name).
  - A WebDAV server can no longer send DTDs or an unlimited folder listing.
  - GitHub workflows have read-only default permissions, and the release tag is no longer placed directly in a script.
- Removed "Copy recent code" from the tray menu. It did nothing, and it would have put a code on the clipboard without showing which account it belongs to.
- **Signing is now offline.** The release workflow no longer has the signing key: it builds, tests, records a build attestation and drafts the release. `scripts/sign-release.ps1` signs on the maintainer's computer after checking the attestation and checksums of every file. See [docs/ReleaseProcess.md](docs/ReleaseProcess.md).
- All GitHub Actions are pinned to exact commits.
- **NuGet lock files:** CI fails when a package differs from the recorded version and hash.
- **Fuzzing:** every reader of outside data (setup links, Base32 keys, KeePass exports, vault contents, vault files) is fed randomly damaged input on each build, with a long run every week. Damaged input must be refused cleanly and a damaged vault must never open with changed contents.
- **Daily vault snapshots:** the first save of each day keeps a copy of the vault as it was, and the last 7 are kept (only for vaults in the app's own folder).
- **Process hardening (Windows):** DLLs are not loaded from remote or low-integrity places, system folders come first, legacy extension points are off, Windows Error Reporting does not collect memory dumps, and the data folder is limited to your account and the system.
- Fixed: changing the theme from a background thread could throw.
- Fixed: the automatic lock could be held off by the mouse resting on the window, because the code refresh counted as mouse movement.

## 1.4.0 (2026-11-01)

A release about safety and everyday comfort. Nothing changes in how existing vaults are stored: they open as before.

### Security
- **Sign-in pause after wrong master passwords.** After 5 wrong passwords in a row the sign-in screen pauses for 30 seconds, then 1, 2, 5 and 15 minutes with each further mistake. The counter survives restarting the app, a correct password resets it, and the next unlock tells you how many wrong passwords were entered. Nothing is ever deleted because of wrong passwords.
- **The vault locks when Windows locks or the computer goes to sleep** (on by default), and optionally when the window is minimized.
- **The window can be kept out of screenshots and screen sharing** (Windows, on by default; turn it off in Settings if you need a screenshot).
- **A weak master password is refused when you create a vault.** New master passwords are checked for length, variety, repeats, common passwords and patterns, with a live hint.
- **Recycle bin, 30 days.** Deleting a password moves it to the recycle bin, with its files, where it can be restored. Entries are removed for good after 30 days, or when you empty the bin.
- **Linux:** the data folder is now readable by its owner only (the vault file already was).
- **Signed update check.** Settings can look for a newer version, and "Download and verify" checks the installer's ML-DSA-65 signature and checksum against the project key built into the app, deleting a download that does not verify. Nothing is installed automatically. Looking for updates after you unlock is optional and off by default.
- The About window shows the fingerprint of the release signing key.

### Added
- **Password health** page: weak, reused and old passwords, with an Open button for each. An optional check against known data breaches (Have I Been Pwned, off by default) sends only the first 5 characters of a password's SHA-1 hash.
- **Password strength meter** while you type, and **entry history**: the last ten versions of an entry, so an overwritten password can be found again.
- **Automatic recovery backups**: a minute after a change, a new recovery backup is saved to a folder you choose, and the newest five are kept. It never writes while the vault is locked or empty.
- **Reminders on the codes page** for a missing recovery key, an old recovery backup, an available update, and unlock methods you could add. Each has "Not now", and all can be switched off in Settings.
- Codes page: **search**, **sort** and **rename** (issuer and label only; the secret is untouched).
- Passwords page: **sort** (title, recently changed, folder), **Open site** (http and https only), **keyboard shortcuts** (Ctrl+F, Ctrl+N, Ctrl+B, Ctrl+C, Ctrl+U, Delete), **right-click menus**, **double-click to copy a password**, and **dragging entries onto folders**.
- The folder list **fits the folder names** and can be resized with a divider; its width is remembered.
- **Help** rewritten as a task-based guide with search, and a redesigned **About** window with links to the article, website, source code, releases, security policy, licence and third-party notices.

### Changed
- Copied passwords, codes and the recovery passphrase stay on the clipboard for 30 seconds, 1 minute or 2 minutes (your choice, 2 minutes at first); copying another secret restarts the wait.
- Updated libraries: ZXing.Net 0.16.11, Microsoft.Win32.SystemEvents 10.0, and the GitHub Actions used by the build and release workflows.

### Repository
- Only the owner can change `main` or the `v*` release tags (rulesets), force-pushes are blocked, secret scanning with push protection is on, and the release workflow signs the Linux packages on a Windows runner. See SECURITY.md.

## 1.3.2 (2026-11-01)

### Security
- **Copied secrets are kept out of the Windows clipboard history and the cloud clipboard.** Before, a copied password could stay in Windows' clipboard history (Win+V) and be synced to your other devices even after MasterSafe cleared the clipboard. Copies now carry the markers that tell Windows not to remember them (and, on Linux, that they come from a password manager, which KDE's clipboard manager respects).
- The repository now has rulesets so that only the owner can change `main` or create, move or delete `v*` release tags, and force-pushes to `main` are blocked. Workflows from outside contributors always need approval, and secret scanning push protection is on. See SECURITY.md.

### Changed
- Updated libraries: ZXing.Net 0.16.11, and the GitHub Actions used by the build and release workflows (checkout 7, download-artifact 8, dependency-review 5).
- New setting "Clear the clipboard after copying a password or code": **30 seconds, 1 minute or 2 minutes**. Copied passwords, codes and recovery passphrases now stay on the clipboard for 2 minutes unless you choose otherwise (it was 30 seconds). Copying another secret restarts the wait, so the newest copy always gets the full time, and the clipboard is only cleared if it still holds the secret, so anything else you copied is left alone.

## 1.3.1 (2026-11-01)

### Added
- Recovery: "Create recovery key automatically". MasterSafe chooses a strong random passphrase (30 characters, no look-alike letters), asks where to save the key file, and shows the passphrase once with a Copy button so it can be written down. Nothing is set up if the file is not saved. Choosing your own passphrase still works.

### Fixed
- Choosing an icon for a folder did nothing: the picker closed before the choice was applied. It now changes the icon and then closes.
- Attached file names use the same strict rules on every system (no `:`, `*`, `?` and so on), so a name is safe wherever the vault is opened.
- The release workflow signs the Linux packages on a Windows runner, because the Ubuntu runner cannot create ML-DSA signatures.

## 1.3.0 (2026-11-01)

### Added
- **Attached files** on password entries, like KeePass: attach a file in the entry form (up to 5 MB each, 50 MB in the vault), save it back out, remove it. The files are stored encrypted inside the vault and are included in recovery backups. KeePass XML exports with attachments are imported with them. Vaults that have no attachments keep the old file layout, so older versions can still open them.
- **Folder icons**: pick an icon for each password folder from a set of 24. The icons come from Material Design Icons (Apache License 2.0); see THIRD-PARTY-NOTICES.md. They are shown in the folder list and in the folder pickers, and they follow a folder when it is renamed.
- Password folders: a folder tree on the Passwords page, with new, rename and delete (deleting keeps the passwords and moves them up), a Folder choice in the entry form, and "Move selected to" for several entries at once. Search also matches the folder. Folders are for passwords only; authenticator accounts stay separate. KeePass and KeePassXC groups are imported as folders. Existing vaults open as before, with every password at the top level.
- Passwords: tick several entries (or "Select all shown") and delete them together, after one confirmation. Changing the search clears the ticks so hidden entries are never deleted.
- The About window links to the author's web site, <https://www.gabriel-vergara.com/>, and the installer lists it as the publisher.
- **Import from KeePass and KeePassXC** (Import page, or drop the file onto the window): KeePass XML (2.x) and KeePassXC CSV or XML exports. Passwords keep their title, user name, URL and notes, and the group becomes a tag. One-time-password fields become authenticator accounts. A summary is shown first, entries already in the vault are skipped, and nothing is added until you confirm. Recycle bin entries, old versions and attachments are not imported. The files are read safely (no DTDs or external entities, size and entry limits). Opening `.kdbx` files directly is still planned.

## 1.2.1 (2026-10-31)

### Fixed
- "Copy code" on the dashboard only showed a message and never put the code on the clipboard. It now copies it (and clears it after 30 seconds, like passwords). A busy clipboard is retried, and a failure is reported instead of ignored.
- Linux: the package shipped an older SkiaSharp native library that did not match the new SkiaSharp 4, so QR codes and drawing would fail. The matching native library is now included.
- Backup names containing a backslash or colon are now refused on every platform, not only Windows.
- The SBOM workflow uses the current CycloneDX option names.
- Dragging a QR code image onto the window now imports it (it did nothing before). The Import page also has a "Choose an image..." button, accepts several images at once, explains why a file was refused, and no longer adds the same account twice.
- The system tray icon did not appear after the Avalonia 12 update. It is now attached to the application so Windows shows it.
- Minimizing the window now also sends it to the tray when "Keep running in the system tray" is on.

### Added
- `MasterSafe-Cli` standalone verification tool, attached to releases (Windows and Linux, no .NET needed). Its
  `verify-release` command checks every installer and package in a folder against its checksum and signature in one step.
- Clear one-line error messages from the command-line tool instead of stack traces.
- The installer and the Linux package no longer include native debug-symbol files (about 110 MB), which SkiaSharp 4 ships.

### Changed
- Updated libraries: **Avalonia 12.1.4** (the UI toolkit), SkiaSharp 4.154, CommunityToolkit.Mvvm 8.4.2, Entity Framework 10
  and SQLitePCLRaw 3.x (data project), test packages. The separate Tmds.DBus.Protocol security pin is gone because Avalonia 12
  already requires the fixed version.
- Updated the GitHub Actions used by the build and release workflows (setup-dotnet 6, upload-artifact 7, CodeQL 4, gitleaks 3,
  action-gh-release 3).

## 1.2.0 (2026-10-31)

### Added
- **Recovery.** A recovery key (a passphrase-protected `.msrk` file) and recovery backups of your accounts and
  passwords, encrypted with a hybrid post-quantum scheme (ECDH P-384 + ML-KEM-768).
- **Forgot your master password.** Restore from a recovery backup, or start a new vault. The old vault is moved to an
  archive, never deleted, and can be opened again later.
- **Vaults page.** Shows where your files are stored, opens any vault file (for example from a USB stick),
  opens archived vaults and deletes old ones after a confirmation.
- **FIDO2 security keys** and **Windows Hello** as ways to unlock besides the master password (Windows).
- **Vault format 3** with separate unlock slots. Changing the master password no longer re-encrypts the vault.
- Settings (theme, tray, auto-lock) are remembered between runs. Auto-lock after inactivity now works.
- New sidebar layout, field labels, empty states, show/hide password buttons, splash window and logo.
- Windows installer and Linux package. Release files carry SHA-256 checksums and ML-DSA-65 signatures; the public key is
  in `signing/`.
- Documentation: file locations, roadmap (KeePass import plan, patch process), OWASP self-assessment.

### Changed
- New master passwords and recovery passphrases need at least 12 characters.
- The product is now called **MasterSafe Authenticator** (formerly SafeAuthenticator).
- Argon2id defaults raised to 128 MiB memory, 3 passes, 4 lanes. Existing vaults keep working and are upgraded when
  unlocked with the master password.
- Licence: freeware for personal, non-commercial use.

### Security
- Vault files are saved atomically and a `.bak` copy of the previous version is kept.
- Vault headers and slots are authenticated; file sizes, Argon2 parameters, decompressed sizes and QR image sizes are
  limited before any work is done.
- Locking wipes the encryption key and decrypted data from memory.
- Backup names can no longer point outside the backup folder; WebDAV addresses must use https.
- The old "remember me" feature, which stored the vault key in Windows Credential Manager, was removed.
- Every build now fails on known vulnerable packages; the patched Tmds.DBus.Protocol is used on Linux.

### Known limits
- Hardware unlock (Windows Hello, security keys) is Windows only for now. One security key per vault.
- The Windows installer is not Authenticode-signed, so Edge and Windows SmartScreen show an "unsafe download" warning.
  Steps to continue and how to verify the file are in the README.
- No KeePass import yet. See [docs/Roadmap.md](docs/Roadmap.md).

## 1.1.0

- Real vault login with a vault file, password entries, a password generator, change master password,
  clipboard auto-clear and a quick-unlock option (replaced in 1.2.0).

## 1.0.0

- First release: TOTP and HOTP codes, QR and `otpauth://` import, encrypted vault, encrypted backups.
