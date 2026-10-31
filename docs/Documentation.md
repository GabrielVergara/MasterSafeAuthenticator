# SafeAuthenticator Documentation

**Version:** 1.1.0  
**Author:** Gabriel Vergara Ezcurdia

SafeAuthenticator is a privacy-first, offline-first authenticator (TOTP/HOTP) with an encrypted
vault that also stores KeePass-style password entries.

## 1. Solution layout

```mermaid
flowchart TD
    App[SafeAuthenticator.App<br/>Avalonia MVVM UI] --> Core[SafeAuthenticator.Core<br/>OTP + PasswordEntry]
    App --> Crypto[SafeAuthenticator.Crypto<br/>Argon2id + AES-GCM vault]
    App --> Platform[SafeAuthenticator.Platform<br/>Tray + Credential Manager]
    App --> Backup[SafeAuthenticator.Backup<br/>Local + WebDAV]
    App --> QR[SafeAuthenticator.QR]
    Crypto --> Core
    Backup --> Crypto
    Data[SafeAuthenticator.Data<br/>EF Core SQLite] --> Core
    PQ[SafeAuthenticator.PostQuantum] --> Crypto
    Tests[SafeAuthenticator.Tests] -.-> App
```

## 2. Unlocking the vault

```mermaid
sequenceDiagram
    actor U as User
    participant L as LoginViewModel
    participant M as MainWindowViewModel
    participant V as VaultService
    participant F as FileVaultRepository
    U->>L: Enter master password
    L->>M: UnlockRequested(password)
    M->>V: ExistsAsync()
    alt first run
        M->>V: CreateAsync(password)
    end
    M->>V: OpenAsync(password)
    V->>F: LoadAsync()
    V->>V: Argon2id derive key, AES-256-GCM decrypt
    alt wrong password
        V-->>M: InvalidPasswordException
        M-->>L: Error message
    else success
        V-->>M: OTP accounts
        M->>M: Show Dashboard
    end
```

## 3. Vault file format

```mermaid
flowchart LR
    P[Master password] -->|Argon2id + salt| K[256-bit key]
    C[VaultContents JSON<br/>Accounts + Passwords] -->|GZip| Z[Compressed bytes]
    Z -->|AES-256-GCM| E[Nonce + Ciphertext + Tag]
    K --> E
    E --> VF[(vault.json<br/>Version 2)]
```

Version 1 vaults (a bare JSON list of OTP accounts) are still readable and are
upgraded to version 2 on the next save.

## 4. Quick unlock with Windows Credential Manager

```mermaid
flowchart TD
    A[Unlock succeeds] --> B{Remember me?}
    B -- no --> Z[Done]
    B -- yes --> C[Wrap derived key with DPAPI<br/>CurrentUser scope]
    C --> D[(Windows Credential Manager)]
    E[Next launch] --> F{Credential present?}
    F -- yes --> G[Read + DPAPI unprotect] --> H[Open vault]
    F -- no --> I[Ask master password]
    J[Master password changed] --> K[Delete credential]
```

Status: implemented. The session holds only a derived key; "Remember me" stores it via
`ICredentialStore`/`WindowsCredentialStore` (DPAPI-wrapped), and changing the master
password replaces the stored key.

## 5. Vault contents model (implemented: passwords page, generator, 30 s clipboard clear)

```mermaid
classDiagram
    class VaultContents {
        +IReadOnlyList~OtpAccount~ Accounts
        +IReadOnlyList~PasswordEntry~ Passwords
    }
    class OtpAccount {
        +string Issuer
        +string Label
        +string Secret
    }
    class PasswordEntry {
        +Guid Id
        +string Title
        +string UserName
        +string Password
        +string Url
        +string Notes
        +IReadOnlyList~string~ Tags
    }
    VaultContents o-- OtpAccount
    VaultContents o-- PasswordEntry
```

## 6. Conventions

- Every source file starts with a header naming the project and author.
- Public types and members carry XML documentation comments.
- Build settings (version, authors) live in `Directory.Build.props`.
