# Release signing

This folder holds only the **public** key used to verify releases.

- `masterSafe-release.pub`: the ML-DSA-65 public key. Its SHA-256 fingerprint is printed in the README and SECURITY.md.
- The private key never goes in the repository. It lives offline, on the maintainer's computer and in an encrypted offline copy.

Check a download: `MasterSafe.Cli verify <file> signing/masterSafe-release.pub`


## Signing happens offline

Releases are no longer signed by GitHub Actions. The private key must not be stored in the repository, in GitHub secrets or in
a cloud-synced folder. See [docs/ReleaseProcess.md](../docs/ReleaseProcess.md) and `scripts/sign-release.ps1`, which signs a
draft release on the maintainer's computer after checking that the files really came from the release workflow.
