# Roadmap

This is a plan, not a promise. Items move as I learn what users need. Security fixes always come first.

## Done

- Vault format 3 with key slots: master password (Argon2id), Windows Hello, FIDO2 security key.
- Atomic saves, size and parameter limits on every file read, memory wiping on lock, auto-lock.
- Recovery: recovery key file, recovery backups (hybrid post-quantum), restore from the sign-in screen.
- Vault management: where files are stored, archive an old vault, open any vault file, delete old vaults.
- Settings saved between runs. Confirmation before deleting accounts, passwords and vaults.
- Windows installer, Linux package, signed releases (ML-DSA-65), CI on Windows and Linux. (Released as 1.2.0 and 1.2.1.)
- Password folders (passwords only) with user-selectable icons, multiple delete and move, attached files on entries (5 MiB each, 50 MiB per vault) (1.3.0). Recovery key creation with a generated passphrase (1.3.1). Clipboard choices and Windows clipboard-history protection (1.3.2).
- 1.4.0: sign-in pause after wrong passwords, lock on Windows lock or sleep, screen-capture protection, recycle bin and entry history, password strength and health report, optional breach check, signed update check, automatic recovery backups with reminders, search, sort and rename on the codes page, shortcuts, right-click menus, drag and drop, a rewritten Help and About.
- KeePass import step 1: KeePass XML and KeePassXC CSV or XML exports, with a preview screen.
- 1.5.0: accessibility foundation (screen readers, text size, high contrast, keyboard, easier reading), Spanish and Finnish, first-run guide, Security check-up, Argon2id tuned to the computer, quick lock (tray and Ctrl+Alt+L), auto-hide of revealed passwords, daily vault snapshots, process hardening and a private data folder, offline release signing with build attestations, pinned GitHub Actions, NuGet lock files, fuzzing in CI.

## Next (1.6): deep protection

| Item | Why |
| --- | --- |
| Data key rotation | Old copies and exported backups stop being useful after a password change |
| Installed-file check | A signed list of the installed files, verified at start-up, so replaced files are noticed. Moved from 1.5 |
| Hide revealed passwords when the window loses focus | Revealed passwords already hide after 20 seconds |
| Security settings inside the vault | Today `settings.json` can be edited by malware to switch off auto-lock or clipboard clearing |
| Tamper-evident sign-in counter and monotonic clock | The sign-in pause and the idle timer use the wall clock today |
| Keep secrets out of .NET strings | Hold decrypted passwords as wipeable buffers and decrypt only while copying. A large internal change that touches every screen |
| Bind the vault to this computer (optional) | A second key protected by the TPM or Windows DPAPI is needed besides the master password, so a copied vault file cannot be guessed offline from another machine. One extra recovery step when moving computers; losing the TPM means the recovery key is needed. Linux uses Secret Service |
| Hardware-only unlock (optional) | Allow turning off master-password sign-in once a security key and a recovery key exist. The recovery key becomes essential |
| Event history | A small encrypted list of recent security events (failed sign-ins, exports, saved attachments), shown in Settings |
| Recovery key split (any 2 of 3) | So one lost or stolen share is not enough |
| Printable large-print recovery sheet | A recovery copy that does not depend on a file |

The two optional unlock changes come last: they change how a vault is opened and need migration and rollback tests.

## After that (1.7)

| Item | Notes |
| --- | --- |
| **KeePass import, step 2: open `.kdbx` files directly** | See the plan below |
| Several security keys per vault | A spare key means a lost key does not lock you out of the quick unlock |
| Linux hardware unlock | FIDO2 through libfido2 |

## Later

| Item | Notes |
| --- | --- |
| **KeePass export (`.kdbx`)** | So nobody is locked in. Same code as the importer, in reverse |
| Import from Google Authenticator (migration QR), Aegis, 2FAS, Bitwarden JSON | Each with a preview screen |
| macOS build | The code is cross-platform; needs a signed and notarised package |
| Mobile app (Android and iOS) | Shares the Core, Crypto and Backup libraries. Recovery backups already work across devices |
| Reproducible builds | So a download can be checked against the source. Build attestations are already in place |
| Authenticode signing and Store packaging | **Postponed: costs money and is not critical for now.** Reduces the SmartScreen "unsafe download" warning, which depends on signing and on download reputation over time. Options: a standard (OV) code-signing certificate, Azure Artifact Signing (organisations in the EU, individuals only in the USA and Canada), or publishing through the Microsoft Store, where Microsoft signs the package (the app would need MSIX packaging and a plan for where the vault is kept) |

## KeePass support plan

KeePass databases (`.kdbx`) are a documented format, but the best-known .NET library (KeePassLib) is GPL-licensed and
cannot be combined with this project's license, so MasterSafe will read the format itself and use only permissively
licensed helpers (for example BouncyCastle, MIT, for the ChaCha20 and Salsa20 stream ciphers).

### Step 1: KeePass XML and CSV import (done)

- Input: *File, Export, KeePass XML (2.x)* from KeePass or KeePassXC, and KeePassXC CSV.
- Maps entries to MasterSafe password entries: Title, UserName, Password, URL, Notes; the group path becomes the password folder, and attached files (KeePass XML) are imported up to 5 MiB each.
- Entries with a one-time-password field (`otp`, or `TimeOtp-Secret-Base32` with its period, digits and algorithm
  fields) become authenticator accounts.
- A **preview screen** shows counts, duplicates and anything skipped (history, custom icons, attachments over 5 MiB), and
  nothing is saved until you confirm.
- Safe parsing: DTDs and external entities are disabled, document size and entry counts are limited.
- The export file is **unencrypted**. The app warns about this and offers to open the folder so you can delete it.

### Step 2: open `.kdbx` 3.1 and 4.x directly (read only)

- Password, key file, or both. Supports the KDFs KeePass uses (AES-KDF, Argon2d, Argon2id), the outer ciphers
  (AES-256, ChaCha20), the HMAC block stream, gzip, and protected values.
- The same limits as the rest of the app apply before any work is done: Argon2 memory and time limits, file size,
  decompressed size, entry count.
- The database is read once and imported through the same preview screen. MasterSafe does not edit `.kdbx`
  files in place.
- Testing: files created by KeePass 2.x and KeePassXC, plus malformed and hostile samples, in the unit tests and a
  fuzzing job.
- Not planned: YubiKey challenge-response used as a KeePass key, plugins.

### Step 3: export to `.kdbx` 4

- So you can always leave. Argon2id and AES-256 or ChaCha20, written with the same code as the reader and checked by
  opening the result with KeePassXC in the release checklist.

## Security and compliance process

This is how the project keeps itself patched. The detailed checklist is in [OwaspChecklist.md](OwaspChecklist.md).

| What | How | Cadence |
| --- | --- | --- |
| Known vulnerable packages | The build itself fails (NuGet audit, direct and transitive); CI runs `scripts/check-vulnerabilities.sh`; release builds run it first | Every build, plus weekly |
| Updates | Dependabot for NuGet and GitHub Actions; security updates open pull requests as soon as an advisory exists | Weekly, and on advisory |
| Code scanning | CodeQL, secret scanning (Gitleaks), dependency review on pull requests | Every push, weekly |
| .NET runtime | Releases are self-contained, so each release bundles the newest patched .NET 10 runtime available at build time. After every Microsoft security release, a new build is published | Monthly, or sooner for a critical fix |
| Response targets | Critical: fix or mitigation within 2 days. High: 7 days. Medium: 30 days. Low: next release | Best effort for a one-person project |
| Transparency | CycloneDX SBOM attached to every release, advisories listed in [SBOM.md](SBOM.md) | Every release |
| Review | OWASP ASVS self-assessment re-read before each minor release | Each minor release |
