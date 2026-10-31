# MasterSafe Authenticator

A privacy-first, offline-first authenticator for TOTP/HOTP with an encrypted vault for passwords,
targeting **.NET 10** and running on **Windows** and **Linux**. Free for personal, non-commercial use
(see [License](#license)).

by Gabriel Vergara Ezcurdia

See the [changelog](CHANGELOG.md) for what changed in each version.

## Features

- TOTP / HOTP codes (RFC 6238 / RFC 4226; SHA-1, SHA-256, SHA-512; 6 or 8 digits), imported from an
  `otpauth://` link or a QR code image.
- Encrypted vault for authenticator accounts and KeePass-style password entries, with a password generator
  and clipboard auto-clear after 30 seconds.
- Unlock with the master password (**Argon2id**), **Windows Hello** or a **FIDO2 security key** (tested with a
  Thales SafeNet key). Each method is a separate key slot, so changing the master password never re-encrypts the vault.
- Vault contents encrypted with **AES-256-GCM**; the header and slots are authenticated; saves are atomic and
  keep a `.bak` copy.
- Auto-lock after inactivity; locking wipes the key and decrypted data from memory.
- Encrypted `.sauth` backups to a local folder or a WebDAV server (Nextcloud, ownCloud; https only).
- **Recovery**: a recovery key and recovery backups let you get your data back if you forget the master password.
  On the sign-in screen, "Forgot your master password?" can restore from a recovery backup, or start a new vault
  while the old one is kept in an archive (never deleted) and can be opened later if the password is found.
- **Vaults page**: shows where your files are stored, opens any vault file (a USB stick, another folder), and
  deletes old vaults after a confirmation. Settings are saved between runs.
- **Post-quantum**: hybrid ECDH-P384 + ML-KEM-768 recovery-key backups (library), ML-DSA-65 signed releases.
- System tray, light/dark theme, in-app help.

Planned: KeePass import, more than one security key per vault, hardware unlock (Hello, security keys) on Linux, and
a mobile app. See [docs/Roadmap.md](docs/Roadmap.md) and the known gaps in [docs/ThreatModel.md](docs/ThreatModel.md).

## Download and install

Releases are published at <https://github.com/GabrielVergara/MasterSafeAuthenticator/releases>.

| System | File | How |
| --- | --- | --- |
| Windows 10/11 (x64) | `MasterSafe-Authenticator-<version>-win-x64-setup.exe` | Run it. Installs for your user, no administrator needed |
| Linux x64 / arm64 | `MasterSafe-Authenticator-<version>-linux-<arch>.tar.gz` | `tar xzf` the file, then `bash install.sh` (installs to `~/.local`; `bash install.sh --uninstall` removes it) |

### How to verify a download

Every installer and package comes with a `.sha256` checksum file and an **ML-DSA-65** signature (`.sig`), and the
release page also has the project's public key (`masterSafe-release.pub`, also kept in
[signing/](signing/masterSafe-release.pub)) and a small verification tool.

1. Download **everything** from the release page into one folder: the installer or package, its `.sha256` and `.sig`
   files, `masterSafe-release.pub`, and the tool for your system
   (`MasterSafe-Cli-<version>-win-x64.exe` or `MasterSafe-Cli-<version>-linux-x64`).
2. Open a terminal in that folder and run:

```powershell
.\MasterSafe-Cli-<version>-win-x64.exe verify-release . masterSafe-release.pub        # Windows
./MasterSafe-Cli-<version>-linux-x64 verify-release . masterSafe-release.pub          # Linux (chmod +x first)
```

3. Every file must say `OK ... signature valid, checksum ok`, and the key fingerprint printed first must be:

```
SHA-256 fingerprint: 28ad8deb1624d6f04fe9100e25edb2666ad904e02824af14ef2fe6c8d2a74b8a
```

If any line says `FAIL`, do not install or run that file, and please report it (see [SECURITY.md](SECURITY.md)).

The tool cannot vouch for itself: a replaced tool could print anything. For the strongest check, build it from source
(`dotnet run --project src/MasterSafe.Cli -c Release -- verify-release <folder> signing/masterSafe-release.pub`) and
compare the fingerprint with the one printed here. Single files can also be checked with `verify <file> <pubkey>`, and
a key with `fingerprint <pubkey>`.

### Windows or Edge says the download is unsafe

Microsoft Edge and Windows SmartScreen judge downloads by **reputation**. MasterSafe Authenticator is new, has few
downloads and its installer is not yet signed with a Windows code-signing (Authenticode) certificate, so it is shown as
"not commonly downloaded". That is not a malware verdict. To install it:

1. In Edge, open the download list, choose **...** next to the file, then **Keep**, then **Show more**, then **Keep anyway**.
2. When Windows shows **Windows protected your PC**, choose **More info**, then **Run anyway**.

Before running it, check that the file is genuine, as described in [How to verify a download](#how-to-verify-a-download).
If a check fails, do not run the file and please report it (see [SECURITY.md](SECURITY.md)).
Windows code signing is on the [roadmap](docs/Roadmap.md); the warning fades as signed releases build reputation.

Your vault is stored in `%LocalAppData%\MasterSafe\vault.sauth` on Windows and `~/.local/share/MasterSafe/vault.sauth`
on Linux. Uninstalling never deletes it. [docs/FileLocations.md](docs/FileLocations.md) lists every file the app
creates and where.

## Build and test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore MasterSafe.slnx
dotnet build MasterSafe.slnx -c Release
dotnet test MasterSafe.slnx -c Release
dotnet run --project src/MasterSafe.App          # start the app
bash scripts/check-vulnerabilities.sh            # fail if any package has a known vulnerability
```

On Windows the build targets `net10.0-windows10.0.19041.0` (needed for Windows Hello). On other systems it
targets `net10.0`. To build for Linux from any machine pass `-p:MasterSafeTargetOs=linux`.

### Build the installers

| Target | Command | Output (`artifacts/`) |
| --- | --- | --- |
| Windows installer | `./installer/build-windows.ps1 -Version 1.2.0 [-SigningKey key.file]` (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`) | `MasterSafe-Authenticator-<v>-win-x64-setup.exe` + `.sha256` (+ `.sig`) |
| Linux x64 | `bash installer/linux/build-linux.sh 1.2.0 linux-x64` | `MasterSafe-Authenticator-<v>-linux-x64.tar.gz` + `.sha256` |
| Linux arm64 | `bash installer/linux/build-linux.sh 1.2.0 linux-arm64` | `MasterSafe-Authenticator-<v>-linux-arm64.tar.gz` + `.sha256` |

All packages are self-contained, so users do not need to install .NET. The Linux script also runs on Windows
(Git Bash). Pushing a tag such as `v1.2.0` runs `.github/workflows/release.yml`, which tests on both systems,
builds, signs (repository secret `RELEASE_SIGNING_KEY`) and creates a draft GitHub release.

### Release signing keys

```bash
dotnet run --project src/MasterSafe.Cli -c Release -- keygen masterSafe-release   # writes .pub and .key
dotnet run --project src/MasterSafe.Cli -c Release -- sign <file> masterSafe-release.key
dotnet run --project src/MasterSafe.Cli -c Release -- verify <file> masterSafe-release.pub
```

Keep the `.key` file private (never commit it). Store its contents in the `RELEASE_SIGNING_KEY` secret.

## Solution layout

```
MasterSafe.slnx
├─ src/MasterSafe.Core         # RFC6238 TOTP, RFC4226 HOTP, Base32, otpauth parser, account/password models
├─ src/MasterSafe.Crypto       # Argon2id, AES-256-GCM, vault format 3 with key slots, atomic file repository
├─ src/MasterSafe.PostQuantum  # ML-KEM-768, hybrid ECDH-P384+ML-KEM-768, ML-DSA-65 release signing
├─ src/MasterSafe.QR           # ZXing.Net + SkiaSharp QR decode/generate
├─ src/MasterSafe.Data         # EF Core SQLite persistence (not used by the app at present)
├─ src/MasterSafe.Backup       # Encrypted .sauth backups (password or recovery key), local + WebDAV
├─ src/MasterSafe.Platform     # System tray abstraction, Windows Hello and FIDO2 security-key unlock providers
├─ src/MasterSafe.App          # Avalonia 11 MVVM desktop UI
├─ src/MasterSafe.Cli          # Release tools: verify-release, verify, fingerprint, keygen, sign (also a demo)
├─ tests/MasterSafe.Tests      # Unit tests (RFC vectors, vault security, view models)
├─ installer/                  # Windows (Inno Setup) and Linux (tar.gz) packaging
└─ docs/                       # Architecture, security, threat model, formats
```

## Security model (summary)

- Vault contents are encrypted with a random 256-bit data key (AES-256-GCM).
- The data key is wrapped once per unlock method: master password (Argon2id), Windows Hello and a FIDO2 security key.
- Files read from disk or the network are treated as untrusted: Argon2 parameters, file sizes and
  decompressed sizes are limited before use.
- Secrets are never written to disk in plaintext. No software is unbreakable; read the
  [threat model](docs/ThreatModel.md) for what is and is not protected.

See [docs/Security.md](docs/Security.md), [docs/ThreatModel.md](docs/ThreatModel.md),
[docs/OwaspChecklist.md](docs/OwaspChecklist.md), [docs/FileLocations.md](docs/FileLocations.md),
[docs/Architecture.md](docs/Architecture.md), [docs/BackupFormat.md](docs/BackupFormat.md),
[docs/PQC.md](docs/PQC.md), [docs/Roadmap.md](docs/Roadmap.md), [docs/Documentation.md](docs/Documentation.md)
and [docs/SBOM.md](docs/SBOM.md).

### Patching and compliance

Every build checks all packages against the NuGet advisory database and fails if one has a known vulnerability.
CI repeats the check on Windows and Linux, weekly scans catch new advisories, Dependabot opens update pull requests,
and release builds bundle the newest patched .NET runtime. The self-assessment against the OWASP ASVS is in
[docs/OwaspChecklist.md](docs/OwaspChecklist.md); it is a self-assessment, not a certification.

## Packages

| Package | Purpose |
| --- | --- |
| Konscious.Security.Cryptography.Argon2 | Argon2id KDF |
| ZXing.Net + SkiaSharp | QR decode/generate |
| Microsoft.EntityFrameworkCore.Sqlite + SQLitePCLRaw | Persistence library |
| Avalonia 11 + CommunityToolkit.Mvvm | Desktop UI |
| xunit / FluentAssertions / Moq / coverlet | Testing |

Post-quantum algorithms (ML-KEM, ML-DSA) come from .NET 10 and the operating system's cryptography provider.
The Windows WebAuthn (FIDO2) bindings are generated at build time by Microsoft.Windows.CsWin32 (Windows builds only).

## Reporting a security problem

Please use the repository's **Security** tab on GitHub (Report a vulnerability) instead of a public issue.

## License

MasterSafe Authenticator is **freeware for personal, non-commercial use**. See [LICENSE](LICENSE).
The source is available for review and personal use; commercial use and distribution of modified
versions are not permitted. Third-party components keep their own licenses.

Copyright (c) 2026 Gabriel Vergara Ezcurdia.
