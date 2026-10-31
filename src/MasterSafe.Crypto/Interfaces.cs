// File:    Interfaces.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Crypto;

/// <summary>
/// Derives a key-encryption-key (KEK) from a master password.
/// </summary>
public interface IMasterPasswordService
{
    /// <summary>
    /// Derives a 32-byte KEK from the password and salt using Argon2id.
    /// </summary>
    byte[] DeriveKey(string masterPassword, byte[] salt, Argon2Parameters? parameters = null);

    /// <summary>Generates a cryptographically secure random salt.</summary>
    byte[] GenerateSalt(int length = 16);
}

/// <summary>
/// Authenticated encryption of arbitrary payloads using AES-256-GCM.
/// </summary>
public interface IEncryptionService
{
    /// <summary>Encrypts <paramref name="plaintext"/> with a 32-byte key.</summary>
    EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);

    /// <summary>Decrypts a previously produced payload. Throws on tamper/wrong key.</summary>
    byte[] Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);
}

/// <summary>
/// Repository abstraction for persisting the encrypted vault blob.
/// </summary>
public interface IVaultRepository
{
    /// <summary>Loads the raw encrypted vault file, or null if none exists.</summary>
    Task<VaultFile?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the encrypted vault file.</summary>
    Task SaveAsync(VaultFile vault, CancellationToken cancellationToken = default);

    /// <summary>Returns true if a vault already exists.</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level vault operations: open, save, add records, rotate keys.
/// </summary>
public interface IVaultService
{
    /// <summary>Creates a brand-new empty vault protected by the master password.</summary>
    Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default);

    /// <summary>Opens the vault, decrypting all records. Throws <see cref="InvalidPasswordException"/> on bad password.</summary>
    Task<IReadOnlyList<MasterSafe.Core.OtpAccount>> OpenAsync(string masterPassword, CancellationToken cancellationToken = default);

    /// <summary>Derives and verifies the vault key for the password (throws <see cref="InvalidPasswordException"/> if wrong).</summary>
    Task<byte[]> DeriveKeyAsync(string masterPassword, CancellationToken cancellationToken = default);

    /// <summary>Opens the vault with an already derived key (quick unlock).</summary>
    Task<VaultContents> OpenWithKeyAsync(byte[] key, CancellationToken cancellationToken = default);

    /// <summary>Saves contents using an already derived key and the existing salt.</summary>
    Task SaveWithKeyAsync(byte[] key, VaultContents contents, CancellationToken cancellationToken = default);

    /// <summary>True when a vault has already been created.</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the vault returning OTP accounts and password entries.</summary>
    Task<VaultContents> OpenContentsAsync(string masterPassword, CancellationToken cancellationToken = default);

    /// <summary>Encrypts and persists the full vault contents (accounts and passwords).</summary>
    Task SaveContentsAsync(string masterPassword, VaultContents contents, CancellationToken cancellationToken = default);

    /// <summary>Encrypts and persists the supplied OTP records, preserving stored passwords.</summary>
    Task SaveAsync(string masterPassword, IEnumerable<MasterSafe.Core.OtpAccount> accounts, CancellationToken cancellationToken = default);

    /// <summary>Re-encrypts the vault under a new master password (key rotation).</summary>
    Task RotateMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
