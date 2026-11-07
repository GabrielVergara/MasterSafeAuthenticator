# Contributing to MasterSafe Authenticator

Thank you for helping. MasterSafe is a one-person project that guards people's passwords and codes, so changes are
reviewed carefully and some ideas will be declined to keep the program small and safe.

## Before you start

- **Security problems:** never in a public issue. Use private reporting, as described in [SECURITY.md](SECURITY.md).
- **Bugs:** open an issue with the bug form. Say the version (it is on the sign-in screen and in About), your system,
  what you did, and what happened. Never paste a vault, a backup, a password, a recovery key or a code secret.
- **Ideas:** open an issue with the idea form first, before writing code, so we can agree on the approach. Check the
  [roadmap](docs/Roadmap.md) and the [comparison with other apps](docs/Comparison.md): some things are left out on purpose.
- **Translations:** see [docs/Translating.md](docs/Translating.md). A native speaker reviewing Spanish or Finnish is very welcome.

## Licence of contributions

MasterSafe is not open source. It is freeware for personal, non-commercial use, and its [LICENSE](LICENSE) does not allow
publishing modified versions. You may still propose changes here. By opening a pull request you confirm that you wrote the
change, or have the right to submit it, and you agree that the author may include it in MasterSafe and distribute it under
the MasterSafe licence, including any later licence the author chooses for MasterSafe. If you cannot agree to this, please
describe the change in an issue instead.

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). On Windows or Linux:

```bash
dotnet build MasterSafe.slnx -c Release
dotnet test MasterSafe.slnx -c Release
bash scripts/check-vulnerabilities.sh
```

To check the Linux build from Windows, run `bash scripts/linux-check.sh` (needs Docker). The installers are described
in the [README](README.md#build-the-installers). Warnings fail the build, so fix them rather than hiding them.

**Never test against your real vault.** The program keeps its data in your user profile (see
[docs/FileLocations.md](docs/FileLocations.md)); tests use temporary folders, and so should any experiment.

## What a good pull request looks like

- **One change per pull request,** with a short description of what it fixes and how you tested it.
- **Tests:** every fix comes with a test that fails without it. Cryptography and file formats need test vectors or
  round-trip tests, and old vaults and backups must keep opening.
- **Security first:** no new network access, telemetry, logging of secrets, or weaker defaults. Secrets are cleared from
  memory and the clipboard as the existing code does. Explain any change to the vault, backup or sync format in
  [docs/BackupFormat.md](docs/BackupFormat.md) or [docs/Security.md](docs/Security.md).
- **Dependencies:** avoid new packages. If one is needed, say why, pin it in the lock files
  (`scripts/update-lock-files.ps1`) and add it to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- **Style:** match the code around your change. Comments explain why, in plain words. Each source file starts with the
  same header as the others.
- **Words on screen:** short, plain English. Add new text to the Spanish and Finnish files, and to the Help, as described
  in [docs/Translating.md](docs/Translating.md).
- **Accessibility:** every control has a name a screen reader can read, and pages still fit at 200% text size. See
  [docs/Accessibility.md](docs/Accessibility.md).
- **Documentation:** update the [CHANGELOG](CHANGELOG.md) under "Not released yet" and any doc your change affects.

Workflows from outside contributors only run after the owner approves them. The owner decides what is merged and makes
every release; releases are signed offline as described in [docs/ReleaseProcess.md](docs/ReleaseProcess.md).

## Conduct

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).
