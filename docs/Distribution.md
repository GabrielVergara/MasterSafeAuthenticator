# Distribution: winget and Flathub

MasterSafe is offered through two free stores besides the GitHub releases:

| Store | Command once accepted | Package id | Files here |
| --- | --- | --- | --- |
| Windows Package Manager | `winget install mastersafe` | `GabrielVergara.MasterSafeAuthenticator` | `packaging/winget/<version>/` |
| Flathub | `flatpak install flathub io.github.GabrielVergara.MasterSafeAuthenticator` | `io.github.GabrielVergara.MasterSafeAuthenticator` | `packaging/flatpak/` |

Both install the **official signed release files**: winget downloads the installer from the GitHub release and checks
its SHA-256; the Flatpak repackages the Linux `.tar.gz`, also checked by SHA-256. Licence 1.2, section 3, allows these
approved repositories to do that. Neither needs a code-signing certificate.

## winget

**First submission (once, by the maintainer):**

1. Check the manifests: `winget validate --manifest packaging\winget\1.7.1`.
2. Test them (needs an administrator once: `winget settings --enable LocalManifestFiles`), with MasterSafe closed:
   `winget install --manifest packaging\winget\1.7.1`. Then `winget list mastersafe` must show 1.7.1.
3. Submit, from your own GitHub account. Either with Komac (`winget install komac`):
   `komac submit packaging\winget\1.7.1`, or by hand: fork `microsoft/winget-pkgs`, copy the three files to
   `manifests/g/GabrielVergara/MasterSafeAuthenticator/1.7.1/` and open a pull request.
4. Microsoft's pipeline installs the package in a clean machine and scans it. A reviewer may comment on the pull
   request; the label "Moderator-Approved" and a merge usually come within a few days. The installer is not
   Authenticode-signed, which winget accepts.

**Later versions** are automatic: `.github/workflows/winget.yml` runs when a release is published, checks that the
release is signed (`.sig` and the signed manifest are present) and opens the update pull request with Komac. It needs
the repository secret `WINGET_TOKEN`: a classic GitHub token of the maintainer's account with the `public_repo` scope
(Settings, Developer settings, Personal access tokens). Without it the workflow only prints a warning. It can also be
run by hand ("Run workflow", with a version).

The workflow also runs every day at 06:17 UTC and decides by itself: it does nothing while the package is not yet in
winget-pkgs (the first pull request is still waiting for a moderator), or when winget-pkgs already has the newest release,
or when a pull request for it is open. Once the first pull request is merged, the next daily run proposes the newest signed
release (for example a version released in the meantime) and opens an issue in this repository to tell the maintainer, who
then changes the README and the web articles from "coming soon" to `winget install mastersafe`.

winget installs for the current user by default, like the installer; `--scope machine` installs for all users. The
in-app update check keeps working, and `winget upgrade mastersafe` updates too. `ProductCode` in the manifest is the
installer's AppId, so winget also recognises copies installed from the GitHub download.

## Scoop

`bucket/mastersafe.json` is a Scoop manifest, kept in this repository so the repository itself is the bucket. Nobody
reviews it, so it is live as soon as it is pushed. Scoop downloads the signed installer from the GitHub release, checks its
SHA-256 and unpacks it (`"innosetup": true`) into `~\scoop\apps\mastersafe`, without running the installer. It adds a Start
menu shortcut. Your vault in `%LocalAppData%\MasterSafe` is separate and `scoop uninstall mastersafe` never touches it.

```
scoop bucket add mastersafe https://github.com/GabrielVergara/MasterSafeAuthenticator
scoop install mastersafe
```

**Each release:** after publishing, run `.\scripts\update-scoop.ps1 -Version X.Y.Z` (it reads the checksum from the release),
test with `scoop install .\bucket\mastersafe.json` and commit the file. A test fails if the manifest is not on the current
version. `scoop update mastersafe` then updates users; the in-app update check also keeps working. A copy from Scoop and a
copy from the installer share the same vault, so install only one of them.

## Flathub

**Flathub's Generative AI policy** (https://docs.flathub.org/docs/for-app-authors/requirements#generative-ai-policy):
the manifest must not contain AI-generated or AI-assisted content, even when disclosed, and the submission pull request,
its description and replies must be written by a person. The files in `packaging/flatpak/` were prepared with an AI
assistant, so they serve as a reference only: the maintainer writes the Flathub manifest by hand. The submission must
also disclose the AI-assisted parts of the application itself (code and documentation) and their approximate extent.

**Status (2026-11-15): on hold.** The requirements page (https://docs.flathub.org/docs/for-app-authors/requirements)
has points that the current plan, repackaging the signed binary release, does not meet:

- **Built entirely from source.** "All source available submissions must be built entirely from source code." MasterSafe is
  source-available, not open source. Exceptions are for well-known vendors. A source build is possible (the
  `org.freedesktop.Sdk.Extension.dotnet10` extension exists for runtime 26.08, and `flatpak-dotnet-generator.py` from
  `flatpak/flatpak-builder-tools` lists the NuGet packages as sources, because there is no network during the build), but it
  is several days of work.
- **Development history.** Very young projects "will generally not be accepted". The repository was created on 2026-10-31.
- **Generative AI.** Most of the code, tests and documentation were written with an AI assistant (60 of the first 85 commits
  carry a `Co-Authored-By` line). It must be disclosed with the affected parts and approximate extent, and reviewers may
  reject on that basis.
- **Translations.** The Spanish and Finnish texts are machine-written and not yet reviewed by native speakers.
- **Licence.** Section 3 of licence 1.2 allows approved repositories to distribute the official signed release files only. A
  source build on Flathub's servers makes other files, so a licence 1.3 would have to allow that.

Until these are solved, Linux users get the `.deb` (Debian, Ubuntu, Mint) or the `.tar.gz` with `install.sh` (and its
`masterSafe-authenticator-uninstall`), both attached to each release. A `.flatpak` bundle for your own distribution can use
the files in `packaging/flatpak/`; the AI rule above is only for submissions to Flathub.

## The .deb package

`installer/linux/build-deb.sh <archive.tar.gz>` repackages the release archive, so the `.deb` holds the same files: the app in
`/opt/masterSafe-authenticator`, the command `/usr/bin/masterSafe-authenticator`, the menu entry and the icon. The release
workflow builds it for x64 and arm64 and the maintainer signs it with the other files. Build one by hand on Windows with
Docker: `docker run --rm -v "%cd%:/w" debian:12 bash /w/installer/linux/build-deb.sh /w/artifacts/<archive>.tar.gz`.
The package name is lowercase (`mastersafe-authenticator`, Debian's rule); the files keep the `masterSafe-authenticator`
names of the `.tar.gz` install. Removing it keeps `~/.local/share/MasterSafe`.

**Before submitting:**

1. Test the Linux build on a real desktop (GNOME or KDE, X11 and Wayland): unlock, add a code from a QR image, copy,
   tray icon, lock, a backup to a folder chosen in the file dialog.
2. The screenshots in `docs/screenshots/` are drawn by `tools/MasterSafe.Screenshots` (the app itself blocks screen
   capture): `dotnet run --project tools/MasterSafe.Screenshots -- docs/screenshots`. It renders the real pages without
   a screen from a demo vault in a temporary folder; your vault is never opened. Push them: the metainfo links to them
   on `main`.
3. Build and lint locally (needs `flatpak`, `flatpak-builder` and the `org.flatpak.Builder` app):

   ```
   cd packaging/flatpak
   flatpak-builder --user --install --force-clean build io.github.GabrielVergara.MasterSafeAuthenticator.yml
   flatpak run io.github.GabrielVergara.MasterSafeAuthenticator
   flatpak run --command=flatpak-builder-lint org.flatpak.Builder manifest io.github.GabrielVergara.MasterSafeAuthenticator.yml
   ```

**Submit:** fork `flathub/flathub`, create a branch from `new-pr`, add the manifest
`packaging/flatpak/io.github.GabrielVergara.MasterSafeAuthenticator.yml` at the top level (everything else comes from the
release archive) and open a pull request against
`new-pr`, titled "Add io.github.GabrielVergara.MasterSafeAuthenticator". The `io.github.GabrielVergara` id is verified through your
GitHub account, so no website file is needed. Answer the reviewers' comments; review usually takes a few weeks. After
the merge Flathub creates `flathub/io.github.GabrielVergara.MasterSafeAuthenticator` and invites you as maintainer.

**Later versions:** the manifest's `x-checker-data` lets Flathub's external data checker open a pull request with the
new URLs and checksums when a GitHub release appears. Before merging it, check that the release is the signed one. The
software centre shows the release notes from the metainfo, so every release adds a `<release>` entry to
`packaging/flatpak/io.github.GabrielVergara.MasterSafeAuthenticator.metainfo.xml` (a test checks this). From 1.7.1 the Linux archive
also contains these files in `flatpak/`, so the Flathub manifest can take them from the archive and only the URL and
checksum change.

**Inside the Flatpak:**

- `FLATPAK_ID` is set, so the app knows Flathub updates it (`AppInfo.UpdatedBy`). It never checks for or downloads
  updates itself; Flathub's rules forbid it. The Updates section in Settings says so.
- Data is in `~/.var/app/io.github.GabrielVergara.MasterSafeAuthenticator/data/MasterSafe` (and `config/MasterSafe`). A vault from
  the `.tar.gz` install (`~/.local/share/MasterSafe`) is not seen; open it with "Open a vault file" or make a full
  backup and restore it.
- Permissions: X11, the GPU, the network (only for what you turn on: breach check, WebDAV, site icons) and the tray.
  No file system access: files are opened and saved through the desktop's file dialogs.
- Windows Hello and FIDO2 security keys exist only on Windows; on Linux the vault is unlocked with the master password.
