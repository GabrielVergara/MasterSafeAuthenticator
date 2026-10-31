# Changelog

All notable changes to MasterSafe Authenticator. The newest version is first. Dates are when the version was built.

## Unreleased

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
