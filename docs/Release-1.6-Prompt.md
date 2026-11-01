# Prompt for release 1.6

Paste everything below the line into a new session in this repository, or work through it one phase at a time.

---

Build MasterSafe Authenticator 1.6 in this repository. Aim: as safe as we can make it, and easy for anyone to use. When the two
conflict, find a design that gives both. If there is none, safety wins and the app explains the reason in plain words.

Read first: `docs/Roadmap.md` (the 1.6 section and the Icons plan), `docs/ThreatModel.md`, `docs/Comparison.md`, `CHANGELOG.md`.

## Rules

- Never claim that the app is the safest, unbreakable or audited, in the app, the docs or anywhere else. Say what it does and
  what it cannot do.
- A vault must never be lost. Older vaults must open, and each new field must be safe if an older version saves the file.
  Test with a 1.5.1 vault.
- Make no network request unless the user asks for it. Keep everything working offline.
- Keep new features off when they could surprise someone or reveal anything. Keep them on when they only add protection.
- Every new message goes through the translation files (`es.txt`, `fi.txt`) with plain wording. Every new control needs an
  accessible name and keyboard access, and must work with high contrast and text size up to 200%.
- Windows-only code goes behind `#if WINDOWS`, and Linux must keep building.
- Treat `settings.json` and every file read from disk or the network as hostile.
- Update every affected doc in the same change: `CHANGELOG.md`, `README.md`, `docs/Documentation.md` (with manual test rows),
  `docs/ThreatModel.md`, `docs/OwaspChecklist.md`, `docs/Roadmap.md`, `docs/Comparison.md`, the Help text.
- Before saying a phase is done, run all of these and report the real results: `dotnet build MasterSafe.slnx -c Release`,
  `dotnet test MasterSafe.slnx -c Release`, and on Docker
  `docker run --rm -v "${PWD}:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash /src/scripts/linux-check.sh`.
- Windows Hello, security keys, the clipboard and screen readers cannot be proven by tests. List what the owner must test by hand.
- Do not commit or push until the owner asks. The signing key is used only by the owner.

## Phase 1: safe foundations

Do these first, in this order. Each one closes a gap found in the 1.5.1 review.

1. **One running copy.** A second start brings the first window forward and exits. Use a per-user lock on Windows and Linux.
   Done when two starts can never both save.
2. **Security settings inside the vault.** Auto-lock time, lock on Windows lock, lock when minimized, clipboard wait, screen
   capture block and the breach check are stored in the encrypted vault. After unlock, the vault's values win. Reuse the
   approach of `RecoveryKeyFingerprint`. An old vault takes its first values from the settings file, once. Vault paths on a
   network share are refused until the user confirms, and the check runs before the file is opened, so Windows sends no network
   login hash. Done when editing `settings.json` cannot turn off any of these once the vault is open.
3. **Notice an older copy of the vault.** Keep a save counter in the vault and a copy outside the vault file. If the vault is
   older, warn in plain words and let the user continue, because restoring a backup is normal. Say in the docs that this
   catches a swapped file, not malware that also changes the counter. It must never block access.
4. **Version bound into the release signature.** Sign a small file listing the file name, version and SHA-256 of each release
   file. The app refuses an update whose signed version differs from the one offered, or is not newer. Older app versions must
   still verify the normal signatures. Update `scripts/sign-release.ps1` and `docs/ReleaseProcess.md`.

## Phase 2: easier and more private screens

5. **Letter icons** for passwords and authenticator accounts, following the Icons plan. No logos and no network use.
6. **Tap to reveal codes**, optional and off by default, so nobody can read codes over your shoulder.
7. **Hide revealed passwords when the window loses focus**, on by default.
8. **More health checks:** duplicate entries, `http://` addresses and expiry dates, offline, with a simple fix for each. Add
   an expiry date field and a gentle reminder.

## Phase 3: website icon

9. **Website icon**, following every rule in the Icons plan, none of which can be loosened: off by default, one password at a
   time on request, preview and accept. Test it against hostile servers, and fuzz the image check. Do this after Phase 2, because
   it is the only new network feature. If any rule cannot be met, drop the feature and say so.

## Phase 4: check the whole release

10. **Attacker review of everything new.** Look for ways in, in this order: hostile files, hostile settings, the update path, the
    website icon, the clipboard, other programs of the same user. Fix what you find, and add a test for each fix.
11. **Prepare for an independent review.** Write `docs/SecurityReview.md`: the scope, the most important code, how to build and
    test, known limits and past findings. A paid or volunteer reviewer should be able to start from this one page.
12. **Refresh `docs/Comparison.md`** from the vendors' own pages, and replace each **?** you can confirm.

## How to finish

Report what was done, what the tests showed, what was not tested and what is still weak. Give the owner a short list of manual
tests. Stop there, and wait to be asked to commit and release.
