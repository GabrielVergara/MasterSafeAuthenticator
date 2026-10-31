# Security Policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**
(<https://github.com/GabrielVergara/MasterSafeAuthenticator/security/advisories/new>).

Please include the version, your operating system, what you did, and what you expected to happen. I will
acknowledge a report as soon as I can, work on a fix, and credit you in the release notes if you wish.

## How quickly problems are fixed

Targets for a one-person project, measured from when a problem is confirmed: critical within 2 days, high within 7,
medium within 30, low in the next release. Every build fails on known vulnerable dependencies, Dependabot proposes
updates weekly and on every new advisory, and releases bundle the newest patched .NET runtime available when they are
built. Details are in [docs/Roadmap.md](docs/Roadmap.md) and the assessment in [docs/OwaspChecklist.md](docs/OwaspChecklist.md).

(Repository maintainer: turn on **Dependabot security updates**, **secret scanning** and **push protection** in the
repository's Security settings.)

## What counts

Weaknesses in how the vault, backups, unlock methods or release signatures protect your data, or that let a
crafted file crash or exhaust the application. The design and its known limits are described in
[docs/Security.md](docs/Security.md) and [docs/ThreatModel.md](docs/ThreatModel.md). Issues that need malware
already running as you while the vault is unlocked are listed there as out of scope.

## How the repository is protected

Only the owner can change this repository. Everyone else can read it, fork it and propose changes in a pull request, and
the owner decides what is merged.

- **Only the owner changes `main`.** A ruleset on the default branch allows updates only to repository administrators
  (the owner). It also blocks deleting `main` and force-pushing to it.
- **Only the owner creates or changes release tags.** Tags that start with `v` cannot be created, moved or deleted by
  anyone else, so a release cannot be replaced behind your back.
- **Workflows from outside contributors need approval** before they run, every time, and the workflow token is read-only.
- **Secrets are checked before they are pushed** (secret scanning with push protection), and Gitleaks runs on every push.
- **Releases are signed.** Even if a download were swapped, `verify-release` shows it is not genuine (see below).

The rulesets are in the repository settings under Rules, Rulesets.

## Verifying downloads

Release files carry a SHA-256 checksum and an ML-DSA-65 signature. The public key is
[signing/masterSafe-release.pub](signing/masterSafe-release.pub). Put every downloaded file, the public key and the
`MasterSafe-Cli` tool from the release page into one folder and run:

```bash
MasterSafe-Cli-<version>-<system> verify-release . masterSafe-release.pub
```

Every file must report `signature valid, checksum ok`. Full steps: [README](README.md#how-to-verify-a-download).

The key's SHA-256 fingerprint is `28ad8deb1624d6f04fe9100e25edb2666ad904e02824af14ef2fe6c8d2a74b8a`.
If a release is ever signed with a different key, treat it as suspect until this page and the README say otherwise.
