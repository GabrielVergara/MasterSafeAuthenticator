// File:    RecoveryKeyService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;

namespace MasterSafe.Backup;

/// <summary>The result of creating a recovery key.</summary>
/// <param name="FileBytes">The recovery key file to save somewhere safe and offline.</param>
/// <param name="PublicKey">The public key used to create recovery backups. Not secret.</param>
/// <param name="KeyId">Identifier shown to recognise the right key file later.</param>
/// <param name="CreatedUtc">Creation time (ISO 8601, UTC).</param>
public sealed record RecoveryKeyCreated(byte[] FileBytes, byte[] PublicKey, string KeyId, string CreatedUtc);

/// <summary>On-disk recovery key file. The public key is readable; the private key is encrypted with a passphrase.</summary>
public sealed class RecoveryKeyFile
{
    /// <summary>Format version (1).</summary>
    public int Version { get; init; } = 1;

    /// <summary>A note shown to anyone who opens the file in a text editor.</summary>
    public string Note { get; init; } = "MasterSafe Authenticator recovery key. Keep this file and its passphrase safe and offline. Anyone who has both can open your recovery backups.";

    /// <summary>Key encapsulation algorithm name.</summary>
    public string Kem { get; init; } = string.Empty;

    /// <summary>Random key identifier (Base64).</summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>Creation time (ISO 8601, UTC).</summary>
    public string CreatedUtc { get; init; } = string.Empty;

    /// <summary>The public key (Base64).</summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>Argon2 salt for the passphrase (Base64).</summary>
    public string Salt { get; init; } = string.Empty;

    /// <summary>Argon2 parameters for the passphrase.</summary>
    public Argon2Parameters Kdf { get; init; } = Argon2Parameters.Default;

    /// <summary>AES-GCM nonce of the wrapped private key (Base64).</summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>AES-GCM tag of the wrapped private key (Base64).</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The private key encrypted with the passphrase (Base64).</summary>
    public string WrappedPrivateKey { get; init; } = string.Empty;
}

/// <summary>
/// Creates and opens recovery key files. A recovery key is a hybrid ECDH-P384 + ML-KEM-768 key pair. The public key
/// creates recovery backups of the vault; the private key, protected by a passphrase, is needed to open them
/// if the master password is lost.
/// </summary>
public sealed class RecoveryKeyService
{
    /// <summary>Shortest passphrase accepted for a recovery key.</summary>
    public const int MinPassphraseLength = 12;

    private const long MaxFileBytes = 64 * 1024;

    private readonly IEncryptionService _encryption;
    private readonly IMasterPasswordService _passwords;
    private readonly IPostQuantumEncryptionProvider _kem;
    private readonly Argon2Parameters _kdf;

    /// <summary>Creates the service. <paramref name="kem"/> should be the hybrid provider.</summary>
    public RecoveryKeyService(
        IEncryptionService encryption,
        IMasterPasswordService passwords,
        IPostQuantumEncryptionProvider kem,
        Argon2Parameters? kdf = null)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
        _kem = kem ?? throw new ArgumentNullException(nameof(kem));
        _kdf = kdf ?? Argon2Parameters.Default;
        _kdf.Validate();
    }

    /// <summary>Creates a new recovery key protected by <paramref name="passphrase"/>.</summary>
    /// <exception cref="ArgumentException">The passphrase is shorter than <see cref="MinPassphraseLength"/>.</exception>
    public RecoveryKeyCreated Create(string passphrase)
    {
        if (passphrase is null || passphrase.Length < MinPassphraseLength)
        {
            throw new ArgumentException($"The passphrase must be at least {MinPassphraseLength} characters.", nameof(passphrase));
        }

        var pair = _kem.GenerateKeyPair();
        var keyId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var created = DateTimeOffset.UtcNow.ToString("O");
        var publicKey = Convert.ToBase64String(pair.PublicKey);
        var salt = _passwords.GenerateSalt();
        var kek = _passwords.DeriveKey(passphrase, salt, _kdf);
        try
        {
            var wrapped = _encryption.Encrypt(pair.PrivateKey, kek, Aad(keyId, _kem.AlgorithmName, publicKey));
            var file = new RecoveryKeyFile
            {
                Kem = _kem.AlgorithmName,
                KeyId = keyId,
                CreatedUtc = created,
                PublicKey = publicKey,
                Salt = Convert.ToBase64String(salt),
                Kdf = _kdf,
                Nonce = Convert.ToBase64String(wrapped.Nonce),
                Tag = Convert.ToBase64String(wrapped.Tag),
                WrappedPrivateKey = Convert.ToBase64String(wrapped.Ciphertext),
            };

            return new RecoveryKeyCreated(
                JsonSerializer.SerializeToUtf8Bytes(file, new JsonSerializerOptions { WriteIndented = true }),
                pair.PublicKey,
                keyId,
                created);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(pair.PrivateKey);
        }
    }

    /// <summary>Reads the public key and identifier from a recovery key file. No passphrase is needed.</summary>
    /// <exception cref="InvalidDataException">The file is not a recovery key.</exception>
    public (byte[] PublicKey, string KeyId, string CreatedUtc) ReadPublicInfo(byte[] fileBytes)
    {
        var file = Parse(fileBytes);
        return (Convert.FromBase64String(file.PublicKey), file.KeyId, file.CreatedUtc);
    }

    /// <summary>Decrypts the private key. The caller must zero the result after use.</summary>
    /// <exception cref="InvalidPasswordException">The passphrase is wrong or the file was changed.</exception>
    /// <exception cref="InvalidDataException">The file is not a recovery key.</exception>
    public byte[] OpenPrivateKey(byte[] fileBytes, string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        var file = Parse(fileBytes);
        file.Kdf.Validate();

        var kek = _passwords.DeriveKey(passphrase, Convert.FromBase64String(file.Salt), file.Kdf);
        try
        {
            return _encryption.Decrypt(
                new EncryptedPayload(
                    Convert.FromBase64String(file.Nonce),
                    Convert.FromBase64String(file.WrappedPrivateKey),
                    Convert.FromBase64String(file.Tag)),
                kek,
                Aad(file.KeyId, file.Kem, file.PublicKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private RecoveryKeyFile Parse(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        if (fileBytes.Length > MaxFileBytes)
        {
            throw new InvalidDataException("This is not a MasterSafe recovery key file.");
        }

        try
        {
            var file = JsonSerializer.Deserialize<RecoveryKeyFile>(fileBytes);
            if (file is null || file.Version != 1 || file.Kem != _kem.AlgorithmName
                || file.KeyId.Length == 0 || file.PublicKey.Length == 0 || file.WrappedPrivateKey.Length == 0)
            {
                throw new InvalidDataException("This is not a MasterSafe recovery key file for this version.");
            }

            return file;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new InvalidDataException("This is not a MasterSafe recovery key file.", ex);
        }
    }

    private static byte[] Aad(string keyId, string kem, string publicKey)
        => Encoding.UTF8.GetBytes($"MasterSafe.RecoveryKey.v1|{keyId}|{kem}|{publicKey}");
}
