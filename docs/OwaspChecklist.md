# OWASP self-assessment

MasterSafe Authenticator is a desktop application, so it is assessed against the parts of the
[OWASP Application Security Verification Standard (ASVS) 4.0.3](https://owasp.org/www-project-application-security-verification-standard/)
that apply to a local, offline program, and against the relevant OWASP cheat sheets. This is a **self-assessment**, not
a certification or an audit. It lists what is met, what is partly met and what is not, so the gaps are visible. It is
re-read before each minor release. Moving the assessment to ASVS 5.0 is on the [roadmap](Roadmap.md).

Legend: **Met** = implemented and covered by tests or CI. **Partial** = implemented with a stated limit.
**Gap** = not done yet. **N/A** = does not apply to an offline single-user program.

## ASVS 4.0.3, chapter by chapter

| Chapter | Requirement (short) | Status | Evidence and notes |
| --- | --- | --- | --- |
| V1 Architecture | Documented threat model and security controls | Met | [ThreatModel.md](ThreatModel.md), [Security.md](Security.md). All crypto sits in one library (`MasterSafe.Crypto`) |
| V1 | Formal data-flow review for each release | Partial | Reviewed informally with each change; no external review yet |
| V2 Authentication | 2.1.1 User-chosen passwords are at least 12 characters | Met | Master password and recovery passphrase both require 12 (`LoginViewModel.MinPasswordLength`, tested) |
| V2 | 2.1.2 / 2.1.3 Long passwords allowed, not truncated | Met | No maximum; the whole string goes into Argon2id |
| V2 | 2.1.7 Check against breached passwords | Partial | New master passwords are checked against a built-in list of very common passwords and repeated patterns and refused when very weak (`PasswordStrength`, tested). Stored passwords can be checked against Have I Been Pwned by the optional **Password health** check: off by default, only the first 5 characters of the SHA-1 hash leave the computer (`BreachChecker`, tested with a fake server) |
| V2 | 2.1.8 Password strength meter | Met | Live meter in the entry form and on the new-vault screen, and a health report for weak, reused and old passwords (`PasswordStrength`, `PasswordHealth`, tested). It is an estimate and says so |
| V2 | 2.1.9 No composition rules | Met | Only length is checked |
| V2 | 2.1.12 Allow showing the password temporarily | Met | Show/hide button on every password box |
| V2 | 2.2 Anti-automation | Met for the sign-in screen | After 5 wrong master passwords sign-in pauses 30 s, then 1, 2, 5 and 15 min; the counter survives restarts; a wrong-attempt notice is shown at the next unlock (`UnlockThrottle`, tested). Offline attacks on a copied vault file cannot be throttled by any app: Argon2id (128 MiB, 3 passes) and a long master password are the defence |
| V2 | 2.4 Credential storage with a memory-hard KDF | Met | Argon2id; OWASP minimum is 19 MiB, 2 passes; this app uses 128 MiB, 3 passes, 4 lanes |
| V2 | 2.5 Credential recovery without weak methods | Met | No security questions or hints. Recovery key with passphrase, see [BackupFormat.md](BackupFormat.md) |
| V2 | 2.8 / 2.9 Cryptographic authenticators | Met | FIDO2 hmac-secret with user verification required |
| V3 Session | 3.3 Session ends after inactivity; secrets cleared | Met | Auto-lock after the configured idle time, when Windows locks or sleeps (default on) and, optionally, on minimize; lock wipes the data key, decrypted data, the health report, a shown recovery passphrase and pending reminders (tested) |
| V4 Access control | Files protected from other users | Met | Relies on the operating system profile permissions on Windows. On Linux the vault file is written with mode 600 and the data folder with mode 700 |
| V5 Validation | 5.1 Input validated | Met | `otpauth://` parser, settings sanitising, file names for backups |
| V5 | 5.2 / 5.3 Untrusted file data is limited and parsed safely | Met | Vault and backups 100 MiB, attachments 5 MiB each and 50 MiB per vault, decompressed 96 MiB, Argon2 limits, QR images 20 MiB and 40 megapixels, KeePass exports 32 MiB and 50,000 entries with DTDs refused, release information 1 MiB, downloads 150 MiB. Container lengths are checked before memory is reserved. All tested with hostile files |
| V5 | 5.5 Safe deserialisation | Met | `System.Text.Json` into plain data types; no type names read from files |
| V6 Cryptography | 6.1 Data classification | Met | [FileLocations.md](FileLocations.md) and [Security.md](Security.md) list what is secret |
| V6 | 6.2 Approved algorithms | Met | AES-256-GCM, Argon2id, HKDF-SHA-256/384, ECDH P-384, ML-KEM-768, ML-DSA-65, SHA-256. SHA-1 appears only in the optional breach lookup, as the service requires, never to protect anything |
| V6 | 6.2 Nonce handling | Partial | Random 96-bit nonces. Safe for far more saves than a person will ever make with one data key |
| V6 | 6.3 Secure random numbers | Met | `RandomNumberGenerator` everywhere |
| V6 | 6.4 Key management | Met | Random data key wrapped per unlock method; no key stored on disk in the clear; keys zeroed after use |
| V6 | 6.4 Keys in memory | Partial | Byte arrays are wiped. Strings typed by the user (the master password) cannot be wiped in .NET |
| V7 Errors, logging | 7.1 No sensitive data in logs or errors | Met | The app writes no log files. Error messages never contain secrets |
| V7 | 7.2 Security event logging | N/A | Deliberately absent: an offline privacy tool keeps no activity record |
| V8 Data protection | 8.2 Client-side data | Met | Clipboard cleared 30 s, 1 or 2 min after the last copy (setting), kept out of the Windows clipboard history and cloud clipboard; nothing cached to disk; screens clear on lock |
| V8 | 8.3 Sensitive private data | Met | Everything at rest is encrypted, including attached files, the recycle bin and entry history; recovery backups are encrypted before leaving the computer. Copies saved out of the app ("Save as...") are not protected and the app says so |
| V8 | Windows clipboard history / screen capture | Met (Windows) | Copies carry the markers that exclude them from Windows clipboard history and the cloud clipboard (checked on a real clipboard); the window is kept out of screenshots and screen sharing with `SetWindowDisplayAffinity` (setting, default on) |
| V9 Communications | 9.1 TLS for all connections | Met | WebDAV requires `https://` (plain http only for the local machine); .NET validates certificates |
| V9 | Certificate pinning | Gap | Not planned; the user chooses the server |
| V10 Malicious code | 10.1 / 10.2 No backdoors, no telemetry | Met | No network use except what the user switches on: a WebDAV server, the optional breach check (api.pwnedpasswords.com) and the optional update check (github.com). Each is off by default or started by the user, and none sends vault data |
| V10 | 10.3 Release integrity | Partial | SHA-256 and ML-DSA-65 signatures on every release (Linux packages signed on a Windows runner), public key and fingerprint in `signing/`, also shown in the About window; the Windows installer is not yet Authenticode-signed |
| V10 | 10.3 Auto-update integrity | Met | There is no automatic update. "Download and verify" checks the ML-DSA-65 signature and checksum against the key built into the app and deletes a download that fails (tested with tampered and wrongly signed files); the user runs the installer |
| V11 Business logic | 11.1 Destructive actions need confirmation | Met | Deleting accounts, passwords and vaults asks first. Vaults are archived, not deleted, when starting over |
| V12 Files | 12.3 Path traversal | Met | Backup names must be plain file names; attachment names are reduced to a plain name with the strictest (Windows) character rules on every system; only the app's own recovery backup names are ever pruned; vaults are only deleted inside the app folder; web addresses opened are limited to http and https |
| V12 | 12.4 Files are not executed | Met | The app never runs user files |
| V13 API | Not an API service | N/A | Client only |
| V14 Configuration | 14.1 Build pipeline | Met | `TreatWarningsAsErrors`, deterministic builds, analysers, CI on Windows and Linux |
| V14 | 14.2 Dependencies | Met | NuGet audit fails the build; `scripts/check-vulnerabilities.sh` in CI and releases; Dependabot (grouped); CodeQL, Gitleaks and dependency review; SBOM per release; third-party notices in the repository |
| V14 | 14.3 No debug information in release | Met | Release builds; no verbose logging |
| V14 | Reproducible builds / provenance | Gap | On the roadmap |
| V14 | 14.1 Source repository protection | Met | Only the owner can change `main` or `v*` tags (rulesets), force-pushes and deletions are blocked, secret scanning with push protection is on, and workflows from outside contributors need approval |

## OWASP Top 10 (2021), as it applies

| Item | Position |
| --- | --- |
| A01 Broken Access Control | No multi-user access; file permissions and vault slots as described above |
| A02 Cryptographic Failures | Modern, authenticated, memory-hard primitives; no custom crypto; PQ hybrid where public keys are used |
| A03 Injection | No SQL or shell with user data; the settings and vault parsers use typed JSON; the file manager call uses an argument list |
| A04 Insecure Design | Threat model first; unlock methods designed as slots; no recovery backdoor |
| A05 Security Misconfiguration | Secure defaults (auto-lock on, https only, hybrid KEM); no configurable weakening of crypto |
| A06 Vulnerable and Outdated Components | The headline control: audit fails the build, weekly scans, Dependabot, runtime rebuilds. Process in [Roadmap.md](Roadmap.md) |
| A07 Identification and Authentication Failures | See V2; sign-in pause after repeated wrong passwords, strength check on new master passwords |
| A08 Software and Data Integrity Failures | Signed releases, SBOM, authenticated vault headers and slots |
| A09 Security Logging and Monitoring Failures | Intentionally no logs (offline, privacy); noted as a deviation |
| A10 Server-Side Request Forgery | Not applicable; outgoing requests go only to the WebDAV server the user typed, api.pwnedpasswords.com for the opt-in breach check and github.com for the update check, all over https, with response sizes limited |

## Other OWASP guidance used

- **Password Storage Cheat Sheet**: Argon2id above the recommended minimum.
- **Cryptographic Storage Cheat Sheet**: AES-GCM, random keys, key separation, no key next to the data.
- **Desktop App Security Top 10**: insecure storage (encrypted), unsafe file handling (limits), insecure update (no
  auto-update), missing integrity (signatures), vulnerable components (audit gate).
- **Dependency and supply chain**: SBOM, pinned patched versions, GitHub dependency review, secret scanning.

## Automated evidence

The controls above are backed by tests that run on every push on Windows and Ubuntu (`tests/MasterSafe.Tests`). The
main ones, by area:

| Area | Tests |
| --- | --- |
| Sign-in pause, strength gate | `UnlockThrottleTests`, `MasterPasswordGateTests` |
| Vault format, attachments, hostile containers | `AttachmentStorageTests`, `VaultSecurityTests`, `VaultContentsTests` |
| Clipboard, history and capture markers | `ClipboardClearSchedulerTests`, `ClipboardSettingTests`, `SecretClipboardTests` |
| Locking and privacy settings | `PrivacySettingsTests`, `UnlockThrottleTests` |
| Strength, health, recycle bin, history | `RecycleBinAndStrengthTests` |
| Breach lookup and update verification | `UpdateAndBreachTests` |
| Imports | `KeePassImporterTests`, `KeePassImportFlowTests`, `DropImportTests`, `QrLimitsTests` |
| Release signing | `ReleaseVerifierTests`, `ReleaseKeyTests`, `PostQuantumTests` |
| Safe links and file names | `HelpAndAboutContentTests`, `PasswordUsabilityTests`, `AttachmentStorageTests` |

CI also runs CodeQL, Gitleaks, dependency review and the vulnerable-package gate. What cannot be tested automatically
(Windows Hello, security keys, the real Windows clipboard history, the tray) is on the manual checklist in
[Documentation.md](Documentation.md), section 8.

## Known gaps, in priority order

1. Authenticode-sign the Windows installer (postponed: cost).
2. Reproducible builds and signed provenance.
3. Move the assessment to ASVS 5.0.
4. Independent security review.
5. Secrets are .NET strings while the vault is unlocked and cannot be wiped; see the hardening ideas in
   [Roadmap.md](Roadmap.md).
