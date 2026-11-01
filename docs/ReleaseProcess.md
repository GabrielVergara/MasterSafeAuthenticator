# Release process

Releases are built by GitHub but **signed only on the maintainer's own computer**. The signing key is never on GitHub.
That way, someone who got into the repository or its workflows still could not produce a release that passes
verification.

## Steps

1. **Prepare.** Update `CHANGELOG.md` (a section `## <version> (<date>)`), the version in `Directory.Build.props`, the
   installer scripts and `docs/Documentation.md`. Run the build and tests. Push to `main` and wait for CI to be green.
2. **Tag.** `git tag v<version>` and `git push origin v<version>`. Only the owner can create `v*` tags.
3. **Build.** The `release` workflow builds the Windows installer and the Linux packages (x64 and arm64) on GitHub, runs
   the tests and the vulnerable-package gate, and records a signed **build attestation** for every file. It creates a
   **draft** release that is clearly marked "not signed yet".
4. **Sign and publish.** On the maintainer's computer:

   ```
   .\scripts\sign-release.ps1 -Tag v<version> -Publish
   ```

   The script downloads the draft's files, then refuses to continue unless **every file has a valid attestation from this
   repository's release workflow** (a file replaced after the build fails), **and every checksum matches**. Only then does
   it sign each installer, package and command-line tool with the offline ML-DSA-65 key, verifies all signatures against
   `signing/masterSafe-release.pub`, uploads the `.sig` files and publishes the release with the changelog as notes.
   Without `-Publish` it signs and leaves the draft for review.
5. **Check.** Download the published files into a new folder and run `MasterSafe.Cli verify-release <folder> signing/masterSafe-release.pub`.
   Install the installer on a test computer.

## The signing key

- It lives in `~/.masterSafe-signing/masterSafe-release.key` on the maintainer's computer, **not** in the repository, not in
  GitHub secrets and not in any cloud sync folder. Keep an encrypted copy offline (for example on a USB stick in a safe place):
  losing it means a new key and a new fingerprint, which users would have to trust again.
- The public key and its SHA-256 fingerprint are in `signing/`, in the README, in SECURITY.md, in the About window and on
  the author's web site, so a swapped key can be noticed.
- ML-DSA keys cannot be held in today's hardware security keys, so the key file is the best protection available. Put it
  on an encrypted drive and sign on a computer you trust.
- If the key is ever lost or exposed: publish a notice, create a new key pair (`MasterSafe.Cli keygen`), update the public key
  and fingerprint everywhere, and release a new version signed with it.

## Why the attestation check matters

Signing proves "the maintainer approved this file". The attestation proves "this file was built by the release workflow of
this repository from that tag". Checking it before signing means the maintainer never signs a file that was swapped or
built somewhere else, even if the draft release itself was tampered with.
