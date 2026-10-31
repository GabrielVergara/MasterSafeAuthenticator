# Release signing

This folder holds only the **public** key used to verify releases.

- `masterSafe-release.pub`: the ML-DSA-65 public key. Its SHA-256 fingerprint is printed in the README and SECURITY.md.
- The private key never goes in the repository. It lives offline and in the `RELEASE_SIGNING_KEY` GitHub secret.

Check a download: `MasterSafe.Cli verify <file> signing/masterSafe-release.pub`
