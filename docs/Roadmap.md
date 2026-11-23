# Roadmap

This is a plan, not a promise. Items move as I learn what users need. Security fixes always come first.

## Done

- Vault format 3 with key slots: master password (Argon2id), Windows Hello, FIDO2 security key.
- Atomic saves, size and parameter limits on every file read, memory wiping on lock, auto-lock.
- Recovery: recovery key file, recovery backups (hybrid post-quantum), restore from the sign-in screen.
- Vault management: where files are stored, archive an old vault, open any vault file, delete old vaults.
- Settings saved between runs. Confirmation before deleting accounts, passwords and vaults.
- Windows installer, Linux package, signed releases (ML-DSA-65), CI on Windows and Linux. (Released as 1.2.0 and 1.2.1.)
- Password folders (passwords only) with user-selectable icons, multiple delete and move, attached files on entries (5 MiB each, 50 MiB per vault) (1.3.0). Recovery key creation with a generated passphrase (1.3.1). Clipboard choices and Windows clipboard-history protection (1.3.2).
- 1.4.0: sign-in pause after wrong passwords, lock on Windows lock or sleep, screen-capture protection, recycle bin and entry history, password strength and health report, optional breach check, signed update check, automatic recovery backups with reminders, search, sort and rename on the codes page, shortcuts, right-click menus, drag and drop, a rewritten Help and About.
- KeePass import step 1: KeePass XML and KeePassXC CSV or XML exports, with a preview screen.
- 1.5.0: accessibility foundation (screen readers, text size, high contrast, keyboard, easier reading), Spanish and Finnish, first-run guide, Security check-up, Argon2id tuned to the computer, quick lock (tray and Ctrl+Alt+L), auto-hide of revealed passwords, daily vault snapshots, process hardening and a private data folder, offline release signing with build attestations, pinned GitHub Actions, NuGet lock files, fuzzing in CI.
- 1.6.0: one running copy, security settings in the vault, network shares refused until confirmed, older vault copies noticed, version bound into the release signature; letter and website icons, hidden codes, your own order and groups, more health checks and expiry dates; notes, cards, identities, extra fields and a tag filter; full encrypted backups, KeePass export, imports from Bitwarden, 1Password, Proton Pass, Aegis, 2FAS and Google Authenticator; sync through your own folder or WebDAV server; a printed emergency sheet; a review pack in [SecurityReview.md](SecurityReview.md).
- 1.8.0: restore a daily copy from the app, the recovery key on paper (QR codes and text) or in 3 parts (any 2 rebuild it), help and About on the sign-in screen, a manual on the website, closable folders, pages that use the window width, a check of the installed program files against a signed list, a sign-in pause and automatic lock timed by the tick count, and a sign-in counter that notices being turned back.
- 1.8.1: a portable Windows zip, and the Microsoft Store package (the Store signs and updates it).
- 1.8.2: move a password folder, with its passwords and subfolders, into another folder or to the top level.
- 1.7.2: a `.deb` package for Debian, Ubuntu and Mint (x64 and arm64), a processor check in the Linux installer; Flathub on hold.
- 1.7.1: signatures checked on Linux without OpenSSL 3.5 (Mint 22, Ubuntu 24.04), install refused while running, a Linux uninstaller, winget and Flathub packages, licence 1.2.
- 1.7.0: a new data key with every new master password, spare security keys, security events, a pause after many quick copies, KeePass databases opened directly, a countdown ring.
- 1.6.2: the logo draws itself on the start screen; licence 1.1 (not open source); a Support MasterSafe button.
- 1.6.1: changing the kind of a new item changes its fields; the About window fits its content and follows the text size; the sign-in screen shows the version.
- 1.5.1: attacker-style review: trusted recovery key kept in the vault, stronger password protection kept when the password changes, every lock and Exit clears a copied secret, process memory protection on Windows and Linux. Linux tests can also run in Docker.

## Now: stores

- **winget** (`winget install mastersafe`): submitted and validated, waiting for a community moderator
  (microsoft/winget-pkgs#480815). A daily workflow proposes later releases once the package is accepted.
- **Scoop** and a **`.deb`** for Debian, Ubuntu and Mint: available since 1.7.2.
- **Flathub: on hold.** It asks for a build from source, a longer history, a manifest written by hand and a licence that allows
  its own builds. See [Distribution.md](Distribution.md).
- **Promotion without money:** release announcements on Bluesky and Mastodon, articles and a feed on the author's site.

The .NET version stays on .NET 10 (LTS, supported until November 2028). .NET 11 is a short-term release that ends at about
the same time, so the next move is .NET 12 (LTS, November 2027). Monthly .NET 10 patches are picked up by every build.

## Done in 1.8: recovery you can see, low risk

| Item | Why |
| --- | --- |
| Restore a snapshot from the app (**done**) | A button on the Vaults page that restores one of the 7 daily copies. The vault in use is copied to the archive first, so nothing is lost. No file copying by hand |
| Recovery key on paper (**done**) | The recovery key printed as QR codes and text, read back from photos, so it does not depend on a USB stick |
| Recovery key in 3 parts (**done**) | Any 2 of 3 part files rebuild the key, which still needs its passphrase, so one lost or stolen part is not enough |
| Installed-file check (**done**) | The build writes `installed-files.txt` (SHA-256 of every program file) into the program folder and as a release file, so the signed release manifest covers it. At start-up the app compares its files with the list; when it may look for updates, it compares the list with the signed release |
| Tamper-evident sign-in counter and monotonic clock (**done**) | The sign-in pause and the automatic lock are timed by the tick count, so changing the clock does not end a pause or delay the lock; the vault remembers the count of wrong passwords per computer, so a counter turned back in the settings file is reported |

## Next (1.9): deep protection

These change how a vault is opened or how secrets sit in memory, so they need migration, rollback and longer tests.

| Item | Why |
| --- | --- |
| Keep secrets out of .NET strings | Hold decrypted passwords as wipeable buffers and decrypt only while copying. A large internal change that touches every screen |
| Key file (optional) | A small file needed alongside the master password, so a stolen vault cannot be guessed without it. Easier to move between computers than binding to one |
| Bind the vault to this computer (optional) | A second key protected by the TPM or Windows DPAPI is needed besides the master password, so a copied vault file cannot be guessed offline from another machine. One extra recovery step when moving computers; losing the TPM means the recovery key is needed. Linux uses Secret Service |
| Hardware-only unlock (optional) | Allow turning off master-password sign-in once a security key and a recovery key exist. The recovery key becomes essential |

The last three change how a vault is opened, so they come last and need migration and rollback tests.

## Next after that

| Item | Notes |
| --- | --- |
| Android test version | First the codes only: open the synced vault, show and copy codes, unlock with a fingerprint. Then passwords. Same vault format and sync |
| Flathub | When the points in [Distribution.md](Distribution.md) are met: a source build, more history and licence 1.3 |
| Linux hardware unlock | FIDO2 through libfido2 |
| Ignore simulated input for copying (optional) | On Windows, input made by another program is marked. MasterSafe could refuse copies started that way. Off by default, because voice control and other assistive tools work like that |

## Later

| Item | Notes |
| --- | --- |
| Encrypted exports of other apps | Aegis, 2FAS and Bitwarden password-protected files, once a plain export is not enough |
| macOS build | The code is cross-platform; needs a signed and notarised package |
| Mobile app (Android and iOS) | Shares the Core, Crypto and Backup libraries. Recovery backups already work across devices |
| Independent security review | The largest gap against the audited apps in [Comparison.md](Comparison.md). Needs funding or a volunteer reviewer, and free audit programmes may expect an open-source license |
| Reproducible builds | So a download can be checked against the source. Build attestations are already in place |
| Tests on xunit v3 | xunit 2.9.3 is marked as legacy (not vulnerable). Version 3 needs the whole test run moved to Microsoft Testing Platform: `global.json` test runner, coverage through Microsoft.Testing.Extensions.CodeCoverage instead of coverlet, and new filter options in CI. One focused change, not mixed with features |
| Authenticode signing and Store packaging | **Postponed: costs money and is not critical for now.** Reduces the SmartScreen "unsafe download" warning, which depends on signing and on download reputation over time. Options: a standard (OV) code-signing certificate, Azure Artifact Signing (organisations in the EU, individuals only in the USA and Canada), or publishing through the Microsoft Store, where Microsoft signs the package (the app would need MSIX packaging and a plan for where the vault is kept) |

## Icons plan

Done in 1.6. Each entry shows the website icon you accepted, or else a letter icon. Folders have 24 general symbols. No
brand logos are used.

### Step 1: letter icons, built in

- The first letter of the name on a coloured tile, with the colour worked out from the name. Nothing is downloaded and no
  logo is copied, so there is nothing to license.
- Icons follow the theme and high contrast, and the entry's name stays the accessible name.

### Rule for brand logos: none unless proven allowed

**A logo ships only when its license clearly allows it and its owner's brand rules allow this use. If either is missing,
restricted or unclear, it is not used at all.** There is no "probably fine".

- Checked 2026-11-01: Simple Icons releases its project under CC0 but says that does not cover every icon. Of its 3,467
  icons, 3,243 have no license on record. None of the major services checked has one, including Bluesky, Mastodon, Facebook,
  Instagram, Threads, X, TikTok, YouTube, Reddit, Discord, Telegram, WhatsApp, Signal, Snapchat, Pinterest, Twitch,
  Tumblr, GitHub, GitLab, Google, Apple, Netflix, Spotify, PayPal, Steam, Dropbox and Proton Mail.
- So **no brand logos are planned**, and the app ships none. A copyright license would not settle trademark use either.
- Service names may still be written as plain text, and a logo can be added if its owner gives written permission.

### Step 2: website icon, optional, strictest settings only

This fetches an icon the site itself publishes, for your own private use. MasterSafe does not distribute it. The
settings below are fixed; none of them can be loosened.

- **Off by default**, and the setting says what it reveals: the site, and anyone watching the network, can see that this
  computer asked for that site's icon.
- **Only when you ask:** a "Use the site's icon" button on one password at a time, while the vault is unlocked. It shows a
  preview and saves only after you accept. No background fetching and no "fetch all".
- **Direct to the site:** no third-party icon service, because one would learn every site in the vault.
- **Only `https://<host>/favicon.ico` is requested.** The saved path, query and user name are never sent, no cookies are
  stored or sent, and no web page is read or parsed.
- **HTTPS with a valid certificate only.** No fallback to HTTP.
- **Public names only:** IP addresses, local names and any name resolving to a private, loopback or link-local address
  are refused. The check runs again on the connection itself, so a site cannot aim the app at this computer or the network.
- **No redirects to another site.** Only the same name with or without `www.`, over HTTPS, and only one.
- **Strict limits:** 5 seconds and 256 KiB. The file must begin with a PNG or ICO signature before any image decoder
  sees it. SVG is refused because it can carry scripts.
- **Stored safely:** decoded with size limits, reduced to a 32 by 32 PNG, and kept inside the encrypted vault. The
  downloaded file itself is never kept.
- **An icon is not proof.** A fake site can copy a real site's icon, so the app never treats it as a sign a site is genuine.
- **Testing:** hostile servers (private addresses, redirects, huge and malformed files) in the unit tests, and the image
  check in the fuzz tests. Vault files with icons are tested against older versions, as attachments were.

## Features other apps have

Compared on 2026-11-01 with the published feature lists of KeePassXC, Aegis, Bitwarden and 1Password. The full table is in [Comparison.md](Comparison.md). This project stays
offline first, so each idea is judged by what it adds to the attack surface as well as what it gives.

**Useful, but each one adds real risk: decide one at a time**

| Feature | Seen in | Risk to weigh |
| --- | --- | --- |
| Browser extension and passkeys | KeePassXC, Bitwarden, 1Password | The largest gap for daily use, and the largest new attack surface: a browser talking to the vault. Needs its own design and review |
| Auto-type | KeePassXC | Can type a password into the wrong window; needs a confirmation |
| SSH agent | KeePassXC, 1Password | Other programs can ask for a key to be used |
| Mobile apps | Bitwarden, 1Password, Aegis | Large effort. Already in Later |
| Emergency access | Bitwarden | Bitwarden does it through its servers. A recovery key split between people gives much of this offline |
| Travel mode | 1Password | Hiding folders on a trip. Hard to do honestly in a single local vault file |

**Not planned**

- Cloud accounts or a hosted service, and anything that makes MasterSafe depend on one. It goes against the offline design.
- Brand icon packs, for the reason above.

## KeePass support plan

KeePass databases (`.kdbx`) are a documented format, but the best-known .NET library (KeePassLib) is GPL-licensed and
cannot be combined with this project's license, so MasterSafe will read the format itself and use only permissively
licensed helpers (for example BouncyCastle, MIT, for the ChaCha20 and Salsa20 stream ciphers).

### Step 1: KeePass XML and CSV import (done)

- Input: *File, Export, KeePass XML (2.x)* from KeePass or KeePassXC, and KeePassXC CSV.
- Maps entries to MasterSafe password entries: Title, UserName, Password, URL, Notes; the group path becomes the password folder, and attached files (KeePass XML) are imported up to 5 MiB each.
- Entries with a one-time-password field (`otp`, or `TimeOtp-Secret-Base32` with its period, digits and algorithm
  fields) become authenticator accounts.
- A **preview screen** shows counts, duplicates and anything skipped (history, custom icons, attachments over 5 MiB), and
  nothing is saved until you confirm.
- Safe parsing: DTDs and external entities are disabled, document size and entry counts are limited.
- The export file is **unencrypted**. The app warns about this and offers to open the folder so you can delete it.

### Step 2: open `.kdbx` 3.1 and 4.x directly (read only, done in 1.7)

- Password, key file, or both. Supports the KDFs KeePass uses (AES-KDF, Argon2d, Argon2id), the outer ciphers
  (AES-256, ChaCha20), the HMAC block stream, gzip, and protected values.
- The same limits as the rest of the app apply before any work is done: Argon2 memory and time limits, file size,
  decompressed size, entry count.
- The database is read once and imported through the same preview screen. MasterSafe does not edit `.kdbx`
  files in place.
- Testing: files created by KeePass 2.x and KeePassXC, plus malformed and hostile samples, in the unit tests and a
  fuzzing job.
- Not planned: YubiKey challenge-response used as a KeePass key, plugins.

### Step 3: export to `.kdbx` 4 (done in 1.6)

- So you can always leave. Argon2id and AES-256. Checked by opening the result with KeePassXC.

## Security and compliance process

This is how the project keeps itself patched. The detailed checklist is in [OwaspChecklist.md](OwaspChecklist.md).

| What | How | Cadence |
| --- | --- | --- |
| Known vulnerable packages | The build itself fails (NuGet audit, direct and transitive); CI runs `scripts/check-vulnerabilities.sh`; release builds run it first. `patch-watch` checks the packages of the published release every day and opens an issue when a new vulnerability is found | Every build, plus daily for the published release |
| Updates | Dependabot for NuGet (one grouped pull request, lock files refreshed by a workflow) and GitHub Actions; security updates open pull requests as soon as an advisory exists | Weekly, and on advisory |
| Code scanning | CodeQL, secret scanning (Gitleaks), dependency review on pull requests | Every push, weekly |
| .NET runtime | Releases are self-contained, so each release bundles the .NET 10 runtime. The release workflow fails unless it is the newest one (`scripts/check-dotnet-patch.sh`), and `patch-watch` opens an issue the day Microsoft publishes a security release newer than the published build | Monthly, or sooner for a critical fix |
| Response targets | Critical: fix or mitigation within 2 days. High: 7 days. Medium: 30 days. Low: next release | Best effort for a one-person project |
| Transparency | CycloneDX SBOM attached to every release, advisories listed in [SBOM.md](SBOM.md) | Every release |
| Review | OWASP ASVS self-assessment re-read before each minor release | Each minor release |
