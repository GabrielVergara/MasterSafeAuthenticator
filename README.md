# SafeAuthenticator

A privacy-first, offline-first authenticator for TOTP/HOTP with an encrypted vault, targeting **.NET 10**.

## Status

| Area | State |
| --- | --- |
| Build | `dotnet build -c Release` — 0 warnings, 0 errors (TreatWarningsAsErrors) |
130 passing
| Security Core (TOTP/HOTP/otpauth) | Implemented |
| Encrypted Vault (Argon2id + AES-256-GCM) | Implemented |
| Post-Quantum abstraction | Abstraction + honest unavailable provider |
| QR import/generation (ZXing.Net + SkiaSharp) | Implemented |
| EF Core SQLite persistence (encrypted secrets) | Implemented |
| Encrypted local backups (`.sauth`) | Implemented |
| Cloud backups (WebDAV: Nextcloud/ownCloud/generic) | Implemented |
| System-tray abstraction | Implemented (Avalonia TrayIcon + headless fallback) |
| Avalonia MVVM desktop UI | Implemented |

## Solution layout

```
SafeAuthenticator.slnx
├─ src/SafeAuthenticator.Core         # RFC6238 TOTP, RFC4226 HOTP, Base32, otpauth parser, account model
├─ src/SafeAuthenticator.Crypto       # Argon2id KEK, AES-256-GCM, vault + interfaces, DPAPI
├─ src/SafeAuthenticator.PostQuantum  # IPostQuantumEncryptionProvider (ML-KEM-768 abstraction)
├─ src/SafeAuthenticator.QR           # ZXing.Net + SkiaSharp QR decode/generate
├─ src/SafeAuthenticator.Data         # EF Core SQLite persistence (encrypted secrets only)
├─ src/SafeAuthenticator.Backup       # Encrypted .sauth export/import + local-folder provider
├─ src/SafeAuthenticator.Platform     # Cross-platform system-tray abstraction
├─ src/SafeAuthenticator.App          # Avalonia 11 MVVM desktop UI
└─ tests/SafeAuthenticator.Tests      # Unit tests with RFC test vectors + ViewModel tests
```

## Build & test

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Security model (summary)

- Master password → **Argon2id** → 32-byte KEK.
- Records → GZip → **AES-256-GCM** (authenticated) → encrypted vault file.
- Secrets are **never** written to disk in plaintext; the vault round-trips only ciphertext.
- Optional Windows **DPAPI** wrapping for defence-in-depth.
- Post-quantum readiness via an abstraction that refuses to fake crypto.

See [docs/Security.md](docs/Security.md), [docs/ThreatModel.md](docs/ThreatModel.md), [docs/Architecture.md](docs/Architecture.md), [docs/BackupFormat.md](docs/BackupFormat.md), and [docs/PQC.md](docs/PQC.md).

## Packages

| Package | Purpose |
| --- | --- |
| Konscious.Security.Cryptography.Argon2 | Argon2id KDF |
| System.Security.Cryptography.ProtectedData | Windows DPAPI |
| ZXing.Net + SkiaSharp | QR decode/generate (ImageSharp avoided for security advisories) |
| Microsoft.EntityFrameworkCore.Sqlite + SQLitePCLRaw 2.1.12 | Persistence (pinned to clear advisories) |
| Avalonia 11 + CommunityToolkit.Mvvm | Desktop UI |
| xunit / FluentAssertions / Moq | Testing |
| coverlet.collector | Coverage |

## Desktop app

The Avalonia MVVM app (`src/SafeAuthenticator.App`) provides login/unlock, a live dashboard with
auto-refreshing OTP codes and progress rings, account editor with validation, QR/URI import,
encrypted backup management, and theme (System/Light/Dark) settings. Run it with:

```bash
dotnet run --project src/SafeAuthenticator.App
```

## Staged / future work

All brief phases are implemented. Backups support both an encrypted local-folder provider and a
WebDAV cloud provider (`WebDavBackupProvider`) that works with Nextcloud/ownCloud/generic WebDAV
endpoints via `HttpClient` — no vendor SDK, only encrypted bytes leave the device. Additional
vendor-specific providers can be added by implementing `IBackupProvider`.

