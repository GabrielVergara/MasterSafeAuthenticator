# Roadmap

This is a plan, not a promise. Items move as I learn what users need. Security fixes always come first.

## Done

- Vault format 3 with key slots: master password (Argon2id), Windows Hello, FIDO2 security key.
- Atomic saves, size and parameter limits on every file read, memory wiping on lock, auto-lock.
- Recovery: recovery key file, recovery backups (hybrid post-quantum), restore from the sign-in screen.
- Vault management: where files are stored, archive an old vault, open any vault file, delete old vaults.
- Settings saved between runs. Confirmation before deleting accounts, passwords and vaults.
- Password folders (passwords only) with user-selectable icons, multiple delete and move, attached files on entries (5 MiB each, 50 MiB per vault) (1.3.0). Recovery key creation with a generated passphrase (1.3.1). Clipboard choices and Windows clipboard-history protection (1.3.2).
- 1.4.0: sign-in pause after wrong passwords, lock on Windows lock or sleep, screen-capture protection, recycle bin and entry history, password strength and health report, optional breach check, signed update check, automatic recovery backups with reminders, shortcuts, right-click menus, drag and drop, sorting, a rewritten Help and About.
- KeePass import step 1: KeePass XML and KeePassXC CSV or XML exports, with a preview screen (in the next release).
- Windows installer, Linux package, signed releases (ML-DSA-65), CI on Windows and Linux. (Released as 1.2.0 and 1.2.1.)

## Next (1.3)

| Item | Why |
| --- | --- |
| Recovery reminders | Show a warning when the last recovery backup is older than 30 days or the vault has changed a lot since |
| Several security keys per vault | A spare key means a lost key does not lock you out of the quick unlock |
| Edit accounts | Rename, change issuer |
| Search and sorting on the codes page | Needed once you have more than a handful of accounts |
| Authenticode signing of the Windows installer | **Postponed: costs money and is not critical for now.** Reduces the SmartScreen "unsafe download" warning, which depends on signing and on download reputation over time. Options: a standard (OV) code-signing certificate, Azure Artifact Signing (organisations in the EU, individuals only in the USA and Canada), or publishing through the Microsoft Store, where Microsoft signs the package (the app would need MSIX packaging and a plan for where the vault is kept) |
| Automatic encrypted backup after changes | To a folder you choose, with the existing backup format |

## Later

| Item | Notes |
| --- | --- |
| **KeePass import, step 2: open `.kdbx` files directly** | See the plan below |
| **KeePass export (`.kdbx`)** | So nobody is locked in. Same code as the importer, in reverse |
| Linux hardware unlock | FIDO2 through libfido2; Secret Service or TPM for a "remember this computer" option |
| Import from Google Authenticator (migration QR), Aegis, 2FAS, Bitwarden JSON | Each with a preview screen |
| macOS build | The code is cross-platform; needs a signed and notarised package |
| Mobile app (Android and iOS) | Shares the Core, Crypto and Backup libraries. Recovery backups already work across devices |
| Translations | English first; Finnish and Spanish are the first candidates |
| Accessibility review | Screen reader names, contrast checks, keyboard-only use |
| Reproducible builds and SLSA provenance | So a download can be checked against the source |
| Fuzzing of the file parsers | Vault, backup, recovery key, KeePass |

## Hardening ideas (toward "unbreakable", without making it harder to use)

No program is unbreakable, but these steps raise the cost of an attack a lot. Each says how much it changes daily use.

| Idea | What it does | Effect on ease of use |
| --- | --- | --- |
| Tune Argon2id to the computer | Measure at vault creation and pick the strongest settings that still unlock in about a second; offer "re-encrypt with stronger settings" later | None: it happens once |
| Bind the vault to this computer (optional) | A second key protected by the TPM or Windows DPAPI is needed besides the master password, so a copied vault file cannot be guessed offline from another machine. Recovery backups still work with the recovery key | One extra recovery step when moving computers |
| Hardware-only unlock (optional) | Allow turning off master-password sign-in once a security key and a recovery key exist | Faster sign-in; the recovery key becomes essential |
| Keep secrets out of .NET strings | Hold decrypted passwords as wipeable buffers and decrypt only while copying | None visible; a large internal change |
| Process hardening | Block code injection (process mitigation policies), exclude the app from crash dumps | None |
| Rotating encrypted vault snapshots | Keep several dated copies of the vault, not only the last `.bak`, against ransomware, sync mistakes and corruption | None; uses disk space |
| Auto-hide revealed passwords | Hide a shown password again after a few seconds or when the window loses focus | Slight |
| Offline signing key | Sign releases only on the maintainer's computer or a hardware key, so a leaked CI secret cannot sign malware; show a second signature as proof | None for users |
| Pin and verify the supply chain | Pin GitHub Actions to commit hashes, publish SLSA build provenance, run OpenSSF Scorecard, require signed commits | None for users |
| Fuzzing in CI | Fuzz the vault, backup, KeePass and QR parsers on every release | None |
| Event history | A small encrypted list of recent security events (failed sign-ins, exports, saved attachments), shown in Settings | Optional reading |
| Authenticode and Store packaging | Signed installer and an MSIX build, so SmartScreen warnings fade | Fewer warnings |

## KeePass support plan

KeePass databases (`.kdbx`) are a documented format, but the best-known .NET library (KeePassLib) is GPL-licensed and
cannot be combined with this project's license, so MasterSafe will read the format itself and use only permissively
licensed helpers (for example BouncyCastle, MIT, for the ChaCha20 and Salsa20 stream ciphers).

### Step 1: KeePass XML and CSV import (done, ships after 1.2.1)

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
