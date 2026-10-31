# Security Policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**
(<https://github.com/GabrielVergara/MasterSafeAuthenticator/security/advisories/new>).

Please include the version, your operating system, what you did, and what you expected to happen. I will
acknowledge a report as soon as I can, work on a fix, and credit you in the release notes if you wish.

## What counts

Weaknesses in how the vault, backups, unlock methods or release signatures protect your data, or that let a
crafted file crash or exhaust the application. The design and its known limits are described in
[docs/Security.md](docs/Security.md) and [docs/ThreatModel.md](docs/ThreatModel.md). Issues that need malware
already running as you while the vault is unlocked are listed there as out of scope.

## Verifying downloads

Release files carry a SHA-256 checksum and an ML-DSA-65 signature. Verify with:

```bash
MasterSafe.Cli verify <downloaded-file> masterSafe-release.pub
```
