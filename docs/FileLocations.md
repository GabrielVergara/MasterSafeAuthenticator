# Where MasterSafe stores files

MasterSafe keeps everything on your own computer. It has no account, no server of its own, no telemetry and no log
files. The Vaults page in the app shows these locations too.

## Your data

| System | Folder |
| --- | --- |
| Windows | `%LocalAppData%\MasterSafe` (for example `C:\Users\you\AppData\Local\MasterSafe`) |
| Linux | `~/.local/share/MasterSafe` (or `$XDG_DATA_HOME/MasterSafe` when that variable is set) |

| File | What it is | Secret? |
| --- | --- | --- |
| `vault.sauth` | Your vault: accounts, passwords and notes, encrypted. This is the one file that matters | Encrypted |
| `vault.sauth.bak` | The version before the last save, kept so a crash cannot destroy your vault | Encrypted |
| `vault.sauth.tmp` | Exists for a moment during a save, then replaces `vault.sauth`. If you ever see it, a save was interrupted; the real vault is intact | Encrypted |
| `settings.json` | Theme, tray, locking, clipboard and privacy choices, the sign-in pause counter, the list of other vault files you opened, the folder for automatic recovery backups, and the recovery **public** key | No secrets |
| `archive\vault-<date>-<time>.sauth` | Old vaults moved aside, for example after a forgotten master password (plus their `.bak` files) | Encrypted |

Never stored on disk: your master password, the vault's encryption key, the codes shown on screen, or the
passphrase of your recovery key.

## Files you choose the place for

| File | Created by | Notes |
| --- | --- | --- |
| Recovery key file `*.msrk` | Recovery page, Create recovery key | Holds the recovery public key and the private key encrypted with your passphrase. Keep it, and its passphrase, away from this computer |
| Recovery backup `recovery-<date>-<time>.sauth` | Recovery page, Create recovery backup | Your accounts and passwords, encrypted so only the recovery key can open them. Safe to keep in the cloud |
| Password backup `vault-<date>-<time>.sauth` | Backup page | Encrypted with your master password. Saved to a folder you pick, or uploaded over https to a WebDAV server |
| Other vault files | You | Use **Open a vault file** to use a vault from a USB stick or another folder. MasterSafe keeps it where it is and never moves or deletes it |

## Where the program is installed

| System | Folder |
| --- | --- |
| Windows installer (default, current user) | `%LocalAppData%\Programs\MasterSafe Authenticator` |
| Windows installer (all users, if you choose it) | `C:\Program Files\MasterSafe Authenticator` |
| Linux `install.sh` | `~/.local/opt/masterSafe-authenticator`, launcher `~/.local/bin/masterSafe-authenticator`, menu entry `~/.local/share/applications/masterSafe-authenticator.desktop`, icon `~/.local/share/icons/hicolor/256x256/apps/masterSafe-authenticator.png` |

Windows also records the install in the registry (the Uninstall list) and creates a Start Menu shortcut.
**Uninstalling never deletes your vault or settings.** Delete the data folder yourself if you want to remove everything.

## Credentials kept by the operating system

| Unlock method | What is stored and where |
| --- | --- |
| Windows Hello | A key pair held by Windows (in the TPM when available), named `MasterSafe.<slot id>`. It is removed when you remove Windows Hello in Settings or delete that vault in the app |
| FIDO2 security key | Nothing on the computer. The credential is not stored on the key either; the vault file holds an id and a salt, which are not secret |

## Moving to another computer

1. Copy `vault.sauth` (it is encrypted, so a USB stick or cloud folder is fine).
2. Install MasterSafe on the other computer, and on the sign-in screen choose **I already have a vault file**.
3. Unlock with the master password. Windows Hello and security keys are tied to a computer or a key, so set them up again.

## Backing up

Copy `vault.sauth` regularly, or use the Backup page. For protection against a forgotten master password, set up a
recovery key (see [Security.md](Security.md)).
