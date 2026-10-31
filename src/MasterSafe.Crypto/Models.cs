// File:    Models.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Crypto;

/// <summary>
/// Argon2id key-derivation parameters.
/// </summary>
public sealed record Argon2Parameters
{
    /// <summary>Memory cost in kibibytes. Default 64 MiB.</summary>
    public int MemorySizeKib { get; init; } = 65536;

    /// <summary>Number of iterations (time cost). Default 3.</summary>
    public int Iterations { get; init; } = 3;

    /// <summary>Degree of parallelism. Default 4.</summary>
    public int DegreeOfParallelism { get; init; } = 4;

    /// <summary>Derived key length in bytes. Default 32 (AES-256).</summary>
    public int KeyLength { get; init; } = 32;

    /// <summary>Default OWASP-aligned parameters.</summary>
    public static Argon2Parameters Default { get; } = new();
}

/// <summary>
/// Result of an AES-256-GCM encryption operation.
/// </summary>
public sealed record EncryptedPayload(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>
/// Serializable on-disk representation of an encrypted vault.
/// </summary>
public sealed class VaultFile
{
    /// <summary>File format version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Argon2id salt (Base64).</summary>
    public string Salt { get; init; } = string.Empty;

    /// <summary>Argon2 parameters used to derive the KEK.</summary>
    public Argon2Parameters KdfParameters { get; init; } = Argon2Parameters.Default;

    /// <summary>AES-GCM nonce (Base64).</summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>AES-GCM authentication tag (Base64).</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Encrypted, compressed records payload (Base64).</summary>
    public string Ciphertext { get; init; } = string.Empty;

    /// <summary>Indicates the ciphertext payload is GZip-compressed before encryption.</summary>
    public bool Compressed { get; init; } = true;
}

/// <summary>
/// Decrypted vault contents (payload format v2): OTP accounts plus KeePass-style password entries.
/// Format v1 stored only a JSON array of OTP accounts; it is still readable.
/// </summary>
public sealed class VaultContents
{
    /// <summary>OTP accounts.</summary>
    public IReadOnlyList<MasterSafe.Core.OtpAccount> Accounts { get; init; } = [];

    /// <summary>Stored password entries.</summary>
    public IReadOnlyList<MasterSafe.Core.PasswordEntry> Passwords { get; init; } = [];
}
