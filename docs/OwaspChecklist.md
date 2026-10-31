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
| V2 | 2.1.7 Check against breached passwords | Gap | Needs the network or a large bundled list. Under consideration as an optional offline list |
| V2 | 2.1.8 Password strength meter | Partial | Length guidance only |
| V2 | 2.1.9 No composition rules | Met | Only length is checked |
| V2 | 2.1.12 Allow showing the password temporarily | Met | Show/hide button on every password box |
| V2 | 2.2 Anti-automation | Partial | Offline file attacks cannot be throttled; Argon2id (128 MiB, 3 passes) makes each guess costly. No lock-out after wrong attempts in the app |
| V2 | 2.4 Credential storage with a memory-hard KDF | Met | Argon2id; OWASP minimum is 19 MiB, 2 passes; this app uses 128 MiB, 3 passes, 4 lanes |
| V2 | 2.5 Credential recovery without weak methods | Met | No security questions or hints. Recovery key with passphrase, see [BackupFormat.md](BackupFormat.md) |
| V2 | 2.8 / 2.9 Cryptographic authenticators | Met | FIDO2 hmac-secret with user verification required |
| V3 Session | 3.3 Session ends after inactivity; secrets cleared | Met | Auto-lock after the configured idle time; lock wipes the data key and decrypted data (tested) |
| V4 Access control | Files protected from other users | Partial | Relies on the operating system profile permissions. On Linux the vault is written with mode 600 |
| V5 Validation | 5.1 Input validated | Met | `otpauth://` parser, settings sanitising, file names for backups |
| V5 | 5.2 / 5.3 Untrusted file data is limited and parsed safely | Met | Vault 16 MiB, downloads 32 MiB, decompressed 64 MiB, Argon2 limits, QR images 20 MiB and 40 megapixels. All tested with hostile files |
| V5 | 5.5 Safe deserialisation | Met | `System.Text.Json` into plain data types; no type names read from files |
| V6 Cryptography | 6.1 Data classification | Met | [FileLocations.md](FileLocations.md) and [Security.md](Security.md) list what is secret |
| V6 | 6.2 Approved algorithms | Met | AES-256-GCM, Argon2id, HKDF-SHA-256/384, ECDH P-384, ML-KEM-768, ML-DSA-65, SHA-256 |
| V6 | 6.2 Nonce handling | Partial | Random 96-bit nonces. Safe for far more saves than a person will ever make with one data key |
| V6 | 6.3 Secure random numbers | Met | `RandomNumberGenerator` everywhere |
| V6 | 6.4 Key management | Met | Random data key wrapped per unlock method; no key stored on disk in the clear; keys zeroed after use |
| V6 | 6.4 Keys in memory | Partial | Byte arrays are wiped. Strings typed by the user (the master password) cannot be wiped in .NET |
| V7 Errors, logging | 7.1 No sensitive data in logs or errors | Met | The app writes no log files. Error messages never contain secrets |
| V7 | 7.2 Security event logging | N/A | Deliberately absent: an offline privacy tool keeps no activity record |
| V8 Data protection | 8.2 Client-side data | Met | Clipboard cleared after 30 seconds; nothing cached to disk; screens clear on lock |
| V8 | 8.3 Sensitive private data | Met | Everything at rest is encrypted; backups are encrypted before leaving the computer |
| V8 | Windows clipboard history / screen capture | Gap | A copied value may be captured by Windows clipboard history before it is cleared |
| V9 Communications | 9.1 TLS for all connections | Met | WebDAV requires `https://` (plain http only for the local machine); .NET validates certificates |
| V9 | Certificate pinning | Gap | Not planned; the user chooses the server |
| V10 Malicious code | 10.1 / 10.2 No backdoors, no telemetry | Met | No network use except the WebDAV server the user configures |
| V10 | 10.3 Release integrity | Partial | SHA-256 and ML-DSA-65 signatures on every release, public key and fingerprint in `signing/`; Windows installer is not yet Authenticode-signed |
| V10 | 10.3 Auto-update integrity | N/A | There is no auto-update; users download and verify releases |
| V11 Business logic | 11.1 Destructive actions need confirmation | Met | Deleting accounts, passwords and vaults asks first. Vaults are archived, not deleted, when starting over |
| V12 Files | 12.3 Path traversal | Met | Backup names must be plain file names (tested with `..`, absolute paths); vaults are only deleted inside the app folder |
| V12 | 12.4 Files are not executed | Met | The app never runs user files |
| V13 API | Not an API service | N/A | Client only |
| V14 Configuration | 14.1 Build pipeline | Met | `TreatWarningsAsErrors`, deterministic builds, analysers, CI on Windows and Linux |
| V14 | 14.2 Dependencies | Met | NuGet audit fails the build; `scripts/check-vulnerabilities.sh` in CI and releases; Dependabot; SBOM per release |
| V14 | 14.3 No debug information in release | Met | Release builds; no verbose logging |
| V14 | Reproducible builds / provenance | Gap | On the roadmap |

## OWASP Top 10 (2021), as it applies

| Item | Position |
| --- | --- |
| A01 Broken Access Control | No multi-user access; file permissions and vault slots as described above |
| A02 Cryptographic Failures | Modern, authenticated, memory-hard primitives; no custom crypto; PQ hybrid where public keys are used |
| A03 Injection | No SQL or shell with user data; the settings and vault parsers use typed JSON; the file manager call uses an argument list |
| A04 Insecure Design | Threat model first; unlock methods designed as slots; no recovery backdoor |
| A05 Security Misconfiguration | Secure defaults (auto-lock on, https only, hybrid KEM); no configurable weakening of crypto |
| A06 Vulnerable and Outdated Components | The headline control: audit fails the build, weekly scans, Dependabot, runtime rebuilds. Process in [Roadmap.md](Roadmap.md) |
| A07 Identification and Authentication Failures | See V2; no lock-out is a known partial gap |
| A08 Software and Data Integrity Failures | Signed releases, SBOM, authenticated vault headers and slots |
| A09 Security Logging and Monitoring Failures | Intentionally no logs (offline, privacy); noted as a deviation |
| A10 Server-Side Request Forgery | Not applicable; the only outgoing request goes to the server the user typed, over https |

## Other OWASP guidance used

- **Password Storage Cheat Sheet**: Argon2id above the recommended minimum.
- **Cryptographic Storage Cheat Sheet**: AES-GCM, random keys, key separation, no key next to the data.
- **Desktop App Security Top 10**: insecure storage (encrypted), unsafe file handling (limits), insecure update (no
  auto-update), missing integrity (signatures), vulnerable components (audit gate).
- **Dependency and supply chain**: SBOM, pinned patched versions, GitHub dependency review, secret scanning.

## Known gaps, in priority order

1. Check new master passwords against a list of breached passwords (offline).
2. Authenticode-sign the Windows installer.
3. Reproducible builds and signed provenance.
4. Lock-out delay after repeated wrong master passwords in the app window.
5. Keep copied values out of Windows clipboard history.
6. Independent security review.
