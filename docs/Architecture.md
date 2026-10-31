# Architecture

```mermaid
flowchart LR
    subgraph Presentation
        UI[Avalonia UI - staged]
        Tray[System Tray - staged]
    end
    subgraph Application
        VS[VaultService]
    end
    subgraph Domain
        Core[Core: TOTP/HOTP/otpauth/Account]
    end
    subgraph Infrastructure
        Crypto[Crypto: Argon2id + AES-GCM + DPAPI]
        Repo[FileVaultRepository]
        PQ[PostQuantum abstraction]
        DB[(SQLite via EF Core - staged)]
    end

    UI --> VS
    Tray --> VS
    VS --> Core
    VS --> Crypto
    VS --> Repo
    Crypto -.future.-> PQ
    Repo -.staged.-> DB
```

## Projects

- **MasterSafe.Core** — pure, dependency-free OTP domain logic.
- **MasterSafe.Crypto** — encryption, KDF, vault orchestration and persistence interfaces.
- **MasterSafe.PostQuantum** — ML-KEM-768 shaped abstraction for future hybrid encryption.
- **MasterSafe.Tests** — xUnit tests with RFC vectors.

## Principles

- Offline-first: no network dependency to generate codes.
- Clean dependency direction: Core has no outward dependencies; Crypto depends on Core.
- Interfaces (`IVaultService`, `IVaultRepository`, `IEncryptionService`, `IMasterPasswordService`)
  enable DI and testability.
