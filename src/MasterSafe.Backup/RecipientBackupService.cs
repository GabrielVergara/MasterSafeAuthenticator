// File:    RecipientBackupService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;

namespace MasterSafe.Backup;

/// <summary>
/// Backups encrypted to a recovery public key instead of a password. The backup is encrypted with a
/// random key, and that key is delivered with hybrid ECDH-P384 + ML-KEM-768 key encapsulation. Anyone
/// can create such a backup from the public key alone (for example a phone or a second computer), but only the
/// holder of the private recovery key can open it, and the scheme is safe against future quantum computers.
/// </summary>
public sealed class RecipientBackupService
{
    private const int KeySize = 32;

    private readonly IEncryptionService _encryption;
    private readonly IPostQuantumEncryptionProvider _kem;

    /// <summary>Creates the service. <paramref name="kem"/> should be the hybrid provider.</summary>
    public RecipientBackupService(IEncryptionService encryption, IPostQuantumEncryptionProvider kem)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _kem = kem ?? throw new ArgumentNullException(nameof(kem));
    }

    /// <summary>Creates a recovery key pair. Keep the private key offline; share only the public key.</summary>
    public PqKeyPair GenerateRecoveryKeyPair() => _kem.GenerateKeyPair();

    /// <summary>Encrypts only the authenticator accounts to <paramref name="recipientPublicKey"/> and uploads them.</summary>
    public Task<string> ExportAsync(
        IEnumerable<OtpAccount> accounts,
        byte[] recipientPublicKey,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return ExportContentsAsync(new VaultContents { Accounts = accounts.ToList() }, recipientPublicKey, provider, cancellationToken);
    }

    /// <summary>
    /// Encrypts everything in the vault (accounts and passwords) to <paramref name="recipientPublicKey"/> and uploads
    /// it. Returns the location.
    /// </summary>
    public async Task<string> ExportContentsAsync(
        VaultContents contents,
        byte[] recipientPublicKey,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(recipientPublicKey);
        ArgumentNullException.ThrowIfNull(provider);

        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var dataKey = RandomNumberGenerator.GetBytes(KeySize);
        var encapsulation = _kem.Encapsulate(recipientPublicKey);
        try
        {
            var json = VaultContentsCodec.Encode(contents);
            var compressed = BoundedGzip.Compress(json);
            CryptographicOperations.ZeroMemory(json);
            var payload = _encryption.Encrypt(compressed, dataKey, ContentAad(id));
            CryptographicOperations.ZeroMemory(compressed);

            var wrapped = _encryption.Encrypt(dataKey, encapsulation.SharedSecret, WrapAad(id));
            var envelope = new RecipientBackupEnvelope
            {
                Id = id,
                Kem = _kem.AlgorithmName,
                KemCiphertext = Convert.ToBase64String(encapsulation.Ciphertext),
                WrapNonce = Convert.ToBase64String(wrapped.Nonce),
                WrapTag = Convert.ToBase64String(wrapped.Tag),
                WrappedKey = Convert.ToBase64String(wrapped.Ciphertext),
                Nonce = Convert.ToBase64String(payload.Nonce),
                Tag = Convert.ToBase64String(payload.Tag),
                Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            };

            var fileName = $"recovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sauth";
            return await provider.UploadAsync(fileName, JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(encapsulation.SharedSecret);
        }
    }

    /// <summary>Downloads a recovery backup and returns only its authenticator accounts.</summary>
    /// <exception cref="InvalidPasswordException">The key is wrong or the file was changed.</exception>
    public async Task<IReadOnlyList<OtpAccount>> ImportAsync(
        string fileName,
        byte[] recipientPrivateKey,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
        => (await ImportContentsAsync(fileName, recipientPrivateKey, provider, cancellationToken).ConfigureAwait(false)).Accounts;

    /// <summary>Downloads a recovery backup and decrypts all of it (accounts and passwords) with the private recovery key.</summary>
    /// <exception cref="InvalidPasswordException">The key is wrong or the file was changed.</exception>
    public async Task<VaultContents> ImportContentsAsync(
        string fileName,
        byte[] recipientPrivateKey,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipientPrivateKey);
        ArgumentNullException.ThrowIfNull(provider);

        var bytes = await provider.DownloadAsync(fileName, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<RecipientBackupEnvelope>(bytes)
            ?? throw new InvalidDataException("The backup is damaged or empty.");

        if (envelope.Version != 2 || envelope.Kem != _kem.AlgorithmName)
        {
            throw new InvalidDataException("This backup was made with a different recovery scheme.");
        }

        var shared = _kem.Decapsulate(recipientPrivateKey, Convert.FromBase64String(envelope.KemCiphertext));
        byte[]? dataKey = null;
        try
        {
            dataKey = _encryption.Decrypt(
                new EncryptedPayload(
                    Convert.FromBase64String(envelope.WrapNonce),
                    Convert.FromBase64String(envelope.WrappedKey),
                    Convert.FromBase64String(envelope.WrapTag)),
                shared,
                WrapAad(envelope.Id));

            var decrypted = _encryption.Decrypt(
                new EncryptedPayload(
                    Convert.FromBase64String(envelope.Nonce),
                    Convert.FromBase64String(envelope.Ciphertext),
                    Convert.FromBase64String(envelope.Tag)),
                dataKey,
                ContentAad(envelope.Id));

            var json = BoundedGzip.Decompress(decrypted);
            CryptographicOperations.ZeroMemory(decrypted);
            try
            {
                // Backups made before passwords were included hold just a JSON array of accounts.
                return json.Length > 0 && json[0] == (byte)'['
                    ? new VaultContents { Accounts = JsonSerializer.Deserialize<List<OtpAccount>>(json) ?? [] }
                    : VaultContentsCodec.Decode(json);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
            if (dataKey is not null)
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
    }

    private byte[] ContentAad(string id) => Encoding.UTF8.GetBytes($"MasterSafe.Backup.v2.Content|{id}|{_kem.AlgorithmName}");

    private byte[] WrapAad(string id) => Encoding.UTF8.GetBytes($"MasterSafe.Backup.v2.Key|{id}|{_kem.AlgorithmName}");
}

/// <summary>On-disk format of a backup encrypted to a recovery public key.</summary>
public sealed class RecipientBackupEnvelope
{
    /// <summary>Format version (2).</summary>
    public int Version { get; init; } = 2;

    /// <summary>Random backup identifier (Base64), bound into every encryption.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Key encapsulation algorithm name.</summary>
    public string Kem { get; init; } = string.Empty;

    /// <summary>The KEM ciphertext (Base64).</summary>
    public string KemCiphertext { get; init; } = string.Empty;

    /// <summary>Nonce of the wrapped data key (Base64).</summary>
    public string WrapNonce { get; init; } = string.Empty;

    /// <summary>Tag of the wrapped data key (Base64).</summary>
    public string WrapTag { get; init; } = string.Empty;

    /// <summary>The data key wrapped with the KEM secret (Base64).</summary>
    public string WrappedKey { get; init; } = string.Empty;

    /// <summary>Nonce of the encrypted accounts (Base64).</summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>Tag of the encrypted accounts (Base64).</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The encrypted, compressed accounts (Base64).</summary>
    public string Ciphertext { get; init; } = string.Empty;
}
