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

## Next (1.6): icons and deep protection

Icons come first: they are visible to everyone and do not change how a vault is opened.

| Item | Why |
| --- | --- |
| Letter icons for passwords and authenticator accounts | Finding the right entry at a glance, with no logos and no network. See the plan below |
| Website icon for a password (optional) | Fetches the site's own icon, only when you ask. Off by default, with only the strictest settings. See the plan below |

The deep protection work follows:

| Item | Why |
| --- | --- |
| Data key rotation | Old copies and exported backups stop being useful after a password change |
| Installed-file check | A signed list of the installed files, verified at start-up, so replaced files are noticed. Moved from 1.5 |
| Hide revealed passwords when the window loses focus | Revealed passwords already hide after 20 seconds |
| Security settings inside the vault | Today `settings.json` can be edited by malware to switch off auto-lock or clipboard clearing |
| Tamper-evident sign-in counter and monotonic clock | The sign-in pause and the idle timer use the wall clock today |
| Keep secrets out of .NET strings | Hold decrypted passwords as wipeable buffers and decrypt only while copying. A large internal change that touches every screen |
| Bind the vault to this computer (optional) | A second key protected by the TPM or Windows DPAPI is needed besides the master password, so a copied vault file cannot be guessed offline from another machine. One extra recovery step when moving computers; losing the TPM means the recovery key is needed. Linux uses Secret Service |
| Hardware-only unlock (optional) | Allow turning off master-password sign-in once a security key and a recovery key exist. The recovery key becomes essential |
| Event history | A small encrypted list of recent security events (failed sign-ins, exports, saved attachments), shown in Settings |
| Recovery key split (any 2 of 3) | So one lost or stolen share is not enough |
| Printable large-print recovery sheet | A recovery copy that does not depend on a file |

The two optional unlock changes come last: they change how a vault is opened and need migration and rollback tests.

## After that (1.7)

| Item | Notes |
| --- | --- |
| **KeePass import, step 2: open `.kdbx` files directly** | See the plan below |
| Several security keys per vault | A spare key means a lost key does not lock you out of the quick unlock |
| Linux hardware unlock | FIDO2 through libfido2 |

## Later

| Item | Notes |
| --- | --- |
| **KeePass export (`.kdbx`)** | So nobody is locked in. Same code as the importer, in reverse |
| Import from Google Authenticator (migration QR), Aegis, 2FAS, Bitwarden JSON | Each with a preview screen |
| macOS build | The code is cross-platform; needs a signed and notarised package |
| Mobile app (Android and iOS) | Shares the Core, Crypto and Backup libraries. Recovery backups already work across devices |
| Reproducible builds | So a download can be checked against the source. Build attestations are already in place |
| Authenticode signing and Store packaging | **Postponed: costs money and is not critical for now.** Reduces the SmartScreen "unsafe download" warning, which depends on signing and on download reputation over time. Options: a standard (OV) code-signing certificate, Azure Artifact Signing (organisations in the EU, individuals only in the USA and Canada), or publishing through the Microsoft Store, where Microsoft signs the package (the app would need MSIX packaging and a plan for where the vault is kept) |

## Icons plan

Today only folders have icons (24 general symbols, no brand logos). Passwords and authenticator accounts have none. Each
entry will show the website icon you accepted, or else a letter icon.

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

Compared on 2026-11-01 with the published feature lists of KeePassXC, Aegis, Bitwarden and 1Password. This project stays
offline first, so each idea is judged by what it adds to the attack surface as well as what it gives.

**Worth doing, low risk**

| Feature | Seen in | Notes |
| --- | --- | --- |
| Export to `.kdbx` and an encrypted export | KeePassXC, Aegis | Nobody is locked in. Already planned |
| Import from 1Password, Bitwarden, Proton Pass and authenticator apps | KeePassXC, Aegis | Each with a preview screen |
| Custom fields and more item types (secure notes, cards, identities) | KeePassXC, Bitwarden, 1Password | Tags already exist in the data, but need a screen |
| Tap to reveal codes | Aegis | Hides codes until clicked, against shoulder surfing |
| Custom order and groups for authenticator accounts | Aegis | |
| More health checks: duplicate items, `http://` addresses, expiry dates | 1Password | Works offline. Weak and reused passwords are already covered |
| Password expiry reminders | 1Password | |
| Printed emergency sheet | | Local alternative to a cloud emergency contact |

**Useful, but each one adds real risk: decide one at a time**

| Feature | Seen in | Risk to weigh |
| --- | --- | --- |
| Browser extension and passkeys | KeePassXC, Bitwarden, 1Password | The largest gap for daily use, and the largest new attack surface: a browser talking to the vault. Needs its own design and review |
| Auto-type | KeePassXC | Can type a password into the wrong window; needs a confirmation |
| SSH agent | KeePassXC, 1Password | Other programs can ask for a key to be used |
| Mobile apps | Bitwarden, 1Password, Aegis | Large effort. Already in Later |
| Encrypted sync through your own folder or WebDAV | KeePassXC users, Bitwarden | Needs conflict handling so that a merge never loses entries |
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

### Step 2: open `.kdbx` 3.1 and 4.x directly (read only)

- Password, key file, or both. Supports the KDFs KeePass uses (AES-KDF, Argon2d, Argon2id), the outer ciphers
  (AES-256, ChaCha20), the HMAC block stream, gzip, and protected values.
- The same limits as the rest of the app apply before any work is done: Argon2 memory and time limits, file size,
  decompressed size, entry count.
- The database is read once and imported through the same preview screen. MasterSafe does not edit `.kdbx`
  files in place.
- Testing: files created by KeePass 2.x and KeePassXC, plus malformed and hostile samples, in the unit tests and a
  fuzzing job.
- Not planned: YubiKey challenge-response used as a KeePass key, plugins.

### Step 3: export to `.kdbx` 4

- So you can always leave. Argon2id and AES-256 or ChaCha20, written with the same code as the reader and checked by
  opening the result with KeePassXC in the release checklist.

## Security and compliance process

This is how the project keeps itself patched. The detailed checklist is in [OwaspChecklist.md](OwaspChecklist.md).

| What | How | Cadence |
| --- | --- | --- |
| Known vulnerable packages | The build itself fails (NuGet audit, direct and transitive); CI runs `scripts/check-vulnerabilities.sh`; release builds run it first | Every build, plus weekly |
| Updates | Dependabot for NuGet and GitHub Actions; security updates open pull requests as soon as an advisory exists | Weekly, and on advisory |
| Code scanning | CodeQL, secret scanning (Gitleaks), dependency review on pull requests | Every push, weekly |
| .NET runtime | Releases are self-contained, so each release bundles the newest patched .NET 10 runtime available at build time. After every Microsoft security release, a new build is published | Monthly, or sooner for a critical fix |
| Response targets | Critical: fix or mitigation within 2 days. High: 7 days. Medium: 30 days. Low: next release | Best effort for a one-person project |
| Transparency | CycloneDX SBOM attached to every release, advisories listed in [SBOM.md](SBOM.md) | Every release |
| Review | OWASP ASVS self-assessment re-read before each minor release | Each minor release |
