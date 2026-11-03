# Security review pack

This page is for an independent reviewer, paid or volunteer. It should be enough to start from. **MasterSafe has not had an
independent review.** Everything so far is the author's own work, a self-assessment against OWASP ASVS and AI-assisted
reviews, none of which replace one. Please report findings privately as described in [SECURITY.md](../SECURITY.md).

## What MasterSafe is

An offline desktop password manager and authenticator for Windows and Linux, written in C# on .NET 10 with Avalonia. One
person maintains it. It has no account and no server. It holds everything in one encrypted file on the user's computer.

## Scope

**In scope, most important first**

| Area | What to look at | Main code |
| --- | --- | --- |
| Vault encryption and key slots | Format 3, associated data, slot handling, wiping | `src/MasterSafe.Crypto/VaultService.cs`, `AesGcmEncryptionService.cs`, `MasterPasswordService.cs` |
| Reading hostile files | Vault files, backups, sync files, exports of other apps | `VaultContentsCodec.cs`, `BoundedGzip.cs`, `FileVaultRepository.cs`, `src/MasterSafe.Core/Import/` |
| Sync and merge | The sync code, sync files, whether a merge can lose or resurrect data | `src/MasterSafe.Backup/SyncService.cs`, `src/MasterSafe.Crypto/VaultMerger.cs` |
| Recovery | Hybrid ECDH-P384 + ML-KEM-768 backups, recovery key file | `src/MasterSafe.Backup/RecipientBackupService.cs`, `RecoveryKeyService.cs`, `src/MasterSafe.PostQuantum/` |
| Hardware unlock | Windows Hello and FIDO2 hmac-secret | `src/MasterSafe.Platform/` |
| Update path | ML-DSA-65 signatures, the signed manifest, version binding | `src/MasterSafe.App/Services/UpdateChecker.cs`, `src/MasterSafe.PostQuantum/ReleaseVerifier.cs`, `scripts/sign-release.ps1` |
| The only feature that talks to a site the user names | Address rules, redirects, image handling | `src/MasterSafe.App/Services/WebsiteIconFetcher.cs` |
| Hostile settings file | Everything read from `settings.json`, network paths | `SettingsStore.cs`, `VaultPathSafety.cs`, `VaultCatalog.cs`, `MainWindowViewModel.Vault.cs` |
| Process and same-user protection | Memory access, DLL planting, one running copy, clipboard | `ProcessHardening.cs`, `SingleInstance.cs`, `ClipboardClearScheduler.cs`, `SecretClipboard.cs` |

**Out of scope:** the `src/MasterSafe.Data` project, which the app does not use for the vault; the author's web site; and
the attackers listed as out of scope in [ThreatModel.md](ThreatModel.md), such as an administrator or malware reading
memory while the vault is unlocked.

## Questions worth a reviewer's time

1. Can anyone with the vault file alone learn anything beyond its size, or change it without being noticed?
2. Can a crafted vault, backup, sync file, export or settings file run code, exhaust memory, hang the program or lose data?
3. Can a merge be made to lose an entry, or bring back one the user deleted for good, without the sync code?
4. Does the sync code's use as a long-lived AES-GCM key hold up (random 96-bit nonces, file name as associated data)?
5. Does `WebsiteIconFetcher` really connect only to public addresses? Is there a way round `ConnectCallback`?
6. Can an old signed installer be installed as an update?
7. Do decrypted secrets stay in memory after locking in a way that the design could avoid? They are known to stay in .NET strings.
8. Is the Windows process protection (`ProcessHardening.cs`) doing what its comments say?

## How to build and test

You need the .NET 10 SDK. Docker is only needed for the Linux check.

```
dotnet build MasterSafe.slnx -c Release
dotnet test MasterSafe.slnx -c Release
docker run --rm -v "${PWD}:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash /src/scripts/linux-check.sh
```

Longer fuzzing: set `MASTERSAFE_FUZZ_ITERATIONS` (for example `200000`) and run the tests with the filter
`Category=Fuzz`. `MASTERSAFE_FUZZ_SEED` repeats a failing run. `tests/MasterSafe.Tests/Fixtures/vault-1.5.1.sauth` is a real
vault written by version 1.5.1; its password is in `Release16Tests.cs`.

CodeQL, secret scanning, dependency review and a weekly long fuzz run are in `.github/workflows`. Packages are pinned by
lock files with hashes. See [OwaspChecklist.md](OwaspChecklist.md) for the requirement-by-requirement status.

Warning for anyone running the app: the data folder is the real one (`%LocalAppData%\MasterSafe`), and a second copy of the
same version only brings the first one forward. Test on a separate user account or in the Docker check.

## Known limits

These are known and need no report, though better ideas are welcome.

- Decrypted secrets are .NET strings while the vault is unlocked, and cannot be wiped reliably. Planned for 1.7.
- Software running as the user while the vault is unlocked can read the screen and log keys. Process protection stops ordinary
  memory reads, not an administrator, and is lifted during a Windows Hello prompt.
- The sign-in pause and the idle timer use the wall clock, and the pause counter is in `settings.json`.
- The save counter copy can be changed by any program of the same user, so it notices a swapped file only.
- On the first unlock of a vault from before 1.6 its security settings come from the settings file, once.
- The master password protects each vault copy for ever: there is no data key rotation yet, so `.bak`, snapshots and old
  exports open with the password they had. Rotation is planned for 1.7.
- Old sync files stay on a server that refuses deletion, and the sync code still opens them. A hostile server can withhold files
  or show an older set, so a computer can miss changes without knowing. It cannot change or forge a file.
- Anyone with the sync code and access to the place can add items to the vault. Items are data; nothing in them runs.
- A vault saved by a version before 1.6 loses its sync settings, security settings, counter, groups and icons. Kinds, fields
  and expiry dates survive in a block in the notes.
- Recovery backups include the sync code and WebDAV password, protected by the recovery key. Password-protected backups,
  `.kdbx` exports and sync files do not.
- A `.kdbx` export is only as strong as the password chosen, and leaves the vault's own protections behind.
- Installers are signed with ML-DSA-65 only. There is no Authenticode signature, so Windows warns about them.
- Linux has only been run in a container, not on a real desktop. There is no hardware unlock on Linux.

## Past findings

Full detail is in [CHANGELOG.md](../CHANGELOG.md).

| Version | Found by | Finding | Fix |
| --- | --- | --- | --- |
| 1.5.0 | Self-review | `.bak` and snapshots survived a password change | Removed on password change |
| 1.5.0 | Self-review | Password boxes gave plain text to accessibility tools | They report dots |
| 1.5.0 | Self-review | A WebDAV server could send DTDs or an unlimited listing | Refused and limited |
| 1.5.1 | Attacker-style review | The recovery key could be swapped in `settings.json` | Fingerprint kept in the vault |
| 1.5.1 | Attacker-style review | A strengthened vault fell back to default settings on a password change | The stronger settings are kept |
| 1.5.1 | Attacker-style review | Exit left a copied secret on the clipboard | Every lock and exit clears it |
| 1.5.1 | Attacker-style review | A release file name could lead outside the download folder | Plain names only |
| 1.6.0 | Attacker-style review | `settings.json` could turn off auto-lock and other protections | Those settings live in the vault |
| 1.6.0 | Attacker-style review | Listing a vault on a share made Windows send a network login hash | Network paths are not touched until the user agrees |
| 1.6.0 | Attacker-style review | The same applied to the automatic backup folder | Refused unless picked by hand in this run |
| 1.6.0 | Attacker-style review | An old signed installer could be offered as new | Signed manifest with the version |
| 1.6.0 | Attacker-style review | A WebDAV download was buffered before its size limit applied | Read as it arrives |
| 1.6.0 | Attacker-style review | A WebDAV server could redirect the login elsewhere | Redirects are not followed |
| 1.6.0 | Attacker-style review | Backup Import read a key-derivation setting without checking it, and threw the result away | Checked first; the result is merged |
| 1.6.0 | Attacker-style review | A sync finishing after a lock could put decrypted data back on the screens | The result is dropped |
| 1.6.0 | Attacker-style review | A hostile counter file could wrap the save counter around | Impossible values are ignored |
| 1.6.0 | Repeated test runs | A wrong last letter of a sync code could decode to the same key | Only the exact code is accepted |
| 1.6.0 | Linux check in Docker | On Linux the data folder could depend on the folder the program was started from | Always the standard folder; a test checks it |
