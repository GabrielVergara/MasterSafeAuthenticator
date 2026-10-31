# Changelog

All notable changes to MasterSafe Authenticator. The newest version is first. Dates are when the version was built.

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
