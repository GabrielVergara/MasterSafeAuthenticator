# Security Model

This page describes what MasterSafe Authenticator protects, how, and where the limits are. It makes no
promise that the software cannot be attacked. See [ThreatModel.md](ThreatModel.md) for what is in and out of scope.

## Key hierarchy (vault format 3)

```mermaid
flowchart TD
    MP[Master password] -->|Argon2id + salt| KEK1[Password key]
    HELLO[Windows Hello] -->|signed challenge + HKDF| KEK2[Hello key]
    FIDO[FIDO2 security key] -->|hmac-secret + HKDF| KEK3[Key from the token]
    KEK1 -->|AES-256-GCM wrap| SLOT1[Password slot]
    KEK2 -->|AES-256-GCM wrap| SLOT2[Hello slot]
    KEK3 -->|AES-256-GCM wrap| SLOT3[Security key slot]
    SLOT1 --> DEK[Random 256-bit data key]
    SLOT2 --> DEK
    SLOT3 --> DEK
    DEK -->|AES-256-GCM| VAULT[Accounts and passwords]
```

The vault contents are encrypted with one random 256-bit **data key**. That key is stored once per unlock
method (a "slot"), wrapped under a key that only that method can produce. Consequences:

- Changing the master password, or adding or removing Windows Hello, never re-encrypts the contents.
- Windows Hello and FIDO2 security keys are real additional ways to unlock. Nothing stored on disk can open
  the vault without the Hello prompt or the physical key (and its PIN), and the master password always keeps working.
- Every encryption binds the vault identifier as associated data, and every slot also binds its own id and
  type. Pieces of one vault cannot be swapped into another, and a slot cannot be relabelled.

## Primitives

| Concern | Choice | Notes |
| --- | --- | --- |
| Password KDF | Argon2id | Default 128 MiB, 3 iterations, 4 lanes, 32-byte salt. Read from the file but limited to 1 GiB, 20 iterations and 32 lanes |
| Symmetric | AES-256-GCM | 12-byte random nonce, 16-byte tag, authenticated |
| Windows Hello | RSA PKCS#1 signature of a stored challenge, then HKDF-SHA-256 | The private key stays in the TPM or Windows' protected store and needs the Hello prompt |
| FIDO2 security key | CTAP2 `hmac-secret` through the Windows WebAuthn API, then HKDF-SHA-256 | A secret inside the key is combined with a random salt. Needs the key, its PIN (user verification is required) and a touch. One key per vault; the credential is not stored on the key |
| Hybrid post-quantum KEM | ECDH P-384 + ML-KEM-768, HKDF-SHA-384 | Used for recovery-key backups, see [PQC.md](PQC.md) |
| Release signatures | ML-DSA-65 (FIPS 204) | `MasterSafe.Cli sign` and `verify` |
| OTP | RFC 6238 / RFC 4226 | SHA-1, SHA-256, SHA-512 |

## Protections in the code

- **Atomic saves.** The new vault is written and flushed to a temporary file and then swapped in, and the
  previous version is kept as `vault.sauth.bak`. A crash or power loss cannot leave half a vault.
- **Untrusted file input.** Argon2 parameters, file size (16 MiB vault, 32 MiB download) and decompressed size
  (64 MiB) are limited before any work is done, so a crafted file cannot exhaust memory or CPU.
- **Wrong key never overwrites.** A save first proves the key can open the existing vault.
- **Locking wipes memory.** Locking (manually or after the idle time set in Settings) zeroes the data key and
  removes accounts, passwords and typed secrets from the screens' models. Keys and password bytes are
  zeroed after use. Managed strings such as the master password the user typed cannot be zeroed by .NET;
  they remain until garbage collected.
- **Backups over https only.** WebDAV addresses must be `https://` (plain `http://` is allowed only on
  the same computer) and may not contain a user name or password.
- **Password policy.** New master passwords and recovery passphrases need at least 12 characters (OWASP ASVS 2.1.1).
- **Safe file names and image sizes.** Backup names must be plain file names, so a crafted name cannot reach outside
  the backup folder; QR images are limited to 20 MiB and 40 megapixels before they are decoded.
- **Confirmation before deletion** of accounts, passwords and vaults.
- **No raw key stored.** The older "remember me" feature, which kept the vault key in Windows Credential
  Manager, was removed. Windows Hello replaces it.

## Forgotten master password and recovery

There is no backdoor, so a forgotten master password cannot be reset. These are the ways forward, in the order to try them:

1. **Another unlock method.** Windows Hello or a security key may still open the vault. Then change the master
   password in Settings.
2. **A recovery backup.** On the Recovery page you create a **recovery key** (a hybrid post-quantum key pair; the
   private half is saved in a file protected by a passphrase) and, now and then, a **recovery backup**: your accounts
   and passwords encrypted so only that key can open them. After a lost password, "Forgot your master password?" on the
   sign-in screen restores the backup into a new vault with a new master password.
3. **Start over, keeping the old vault.** The old vault is moved to the `archive` folder (not deleted). If the password
   is found later, open it from the Vaults page, or delete it for good after a confirmation.

The recovery key is only as safe as the place you keep the file and its passphrase. Keep them away from this computer.

## Several vaults

The app works with one vault file at a time (`Open a vault file` can switch). Switching, archiving or deleting a
vault locks the current session first. Vaults opened from outside the app folder are never moved or deleted by the app.
Deleting a vault also removes the Windows Hello credentials created for it.

## Older vaults

Vault formats 1 and 2 (a key derived directly from the password) can still be opened. They are upgraded to
format 3 the next time the vault is unlocked with the master password.

## Recovery

Recovery requires the master password, a set-up unlock method, or a recovery-key backup. There is no
backdoor. If you lose all of them, the vault cannot be decrypted. Keep an encrypted backup
(see [BackupFormat.md](BackupFormat.md)).

## Reporting a vulnerability

Please report security issues privately through the repository's Security tab on GitHub
(**Report a vulnerability**) instead of opening a public issue.
