// File:    BackupService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.Backup;

/// <summary>
/// Produces encrypted <c>.sauth</c> backups: accounts are GZip-compressed then AES-256-GCM encrypted
/// with an Argon2id-derived key. Plaintext is never written.
/// </summary>
public sealed class BackupService : IBackupService
{
    // Version 1 holds authenticator accounts only. Version 4 holds everything. 2 and 3 are vault files, not backups.
    private const int LegacyBackupVersion = 1;
    private const int FullBackupVersion = 4;

    private readonly IEncryptionService _encryption;
    private readonly IMasterPasswordService _masterPassword;
    private readonly Argon2Parameters _kdf;

    public BackupService(
        IEncryptionService encryption,
        IMasterPasswordService masterPassword,
        Argon2Parameters? kdf = null)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _masterPassword = masterPassword ?? throw new ArgumentNullException(nameof(masterPassword));
        _kdf = kdf ?? Argon2Parameters.Default;
    }

    /// <inheritdoc />
    public async Task<string> ExportAsync(
        IEnumerable<OtpAccount> accounts,
        string masterPassword,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(provider);

        var salt = _masterPassword.GenerateSalt();
        var key = _masterPassword.DeriveKey(masterPassword, salt, _kdf);

        var json = JsonSerializer.SerializeToUtf8Bytes(accounts.ToList());
        var compressed = BoundedGzip.Compress(json);
        var payload = _encryption.Encrypt(compressed, key);

        var envelope = new VaultFile
        {
            Version = 1,
            Salt = Convert.ToBase64String(salt),
            KdfParameters = _kdf,
            Nonce = Convert.ToBase64String(payload.Nonce),
            Tag = Convert.ToBase64String(payload.Tag),
            Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            Compressed = true,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var fileName = $"vault-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sauth";
        return await provider.UploadAsync(fileName, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ExportContentsAsync(
        VaultContents contents,
        string password,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(provider);

        var salt = _masterPassword.GenerateSalt();
        var key = _masterPassword.DeriveKey(password, salt, _kdf);
        try
        {
            var plain = VaultContentsCodec.Encode(contents.DataOnly());
            var compressed = BoundedGzip.Compress(plain);
            CryptographicOperations.ZeroMemory(plain);
            var payload = _encryption.Encrypt(compressed, key, Aad(salt));
            CryptographicOperations.ZeroMemory(compressed);

            var envelope = new VaultFile
            {
                Version = FullBackupVersion,
                Salt = Convert.ToBase64String(salt),
                KdfParameters = _kdf,
                Nonce = Convert.ToBase64String(payload.Nonce),
                Tag = Convert.ToBase64String(payload.Tag),
                Ciphertext = Convert.ToBase64String(payload.Ciphertext),
                Compressed = true,
            };

            var fileName = $"vault-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sauth";
            return await provider.UploadAsync(fileName, JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public async Task<VaultContents> ImportContentsAsync(
        string fileName,
        string password,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var bytes = await provider.DownloadAsync(fileName, cancellationToken).ConfigureAwait(false);
        if (SyncPackage.IsPackage(bytes))
        {
            throw new InvalidDataException("This is a sync file, not a backup. Sync reads it by itself on the same page.");
        }

        VaultFile envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VaultFile>(bytes) ?? throw new InvalidDataException("The backup is damaged or empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The backup is damaged or is not a MasterSafe backup.", ex);
        }

        if (envelope.Version is not (LegacyBackupVersion or FullBackupVersion))
        {
            throw new InvalidDataException("This is not a backup made on the Backup page. A vault file is opened on the Vaults page.");
        }

        // Everything here came from a file, so the limits are checked before any work is done.
        envelope.KdfParameters.Validate();
        var salt = Convert.FromBase64String(envelope.Salt);
        var key = _masterPassword.DeriveKey(password, salt, envelope.KdfParameters);
        try
        {
            var payload = new EncryptedPayload(
                Convert.FromBase64String(envelope.Nonce),
                Convert.FromBase64String(envelope.Ciphertext),
                Convert.FromBase64String(envelope.Tag));

            var decrypted = envelope.Version == FullBackupVersion ? _encryption.Decrypt(payload, key, Aad(salt)) : _encryption.Decrypt(payload, key);
            var plain = envelope.Compressed ? BoundedGzip.Decompress(decrypted) : decrypted;
            try
            {
                return envelope.Version == FullBackupVersion
                    ? VaultContentsCodec.Decode(plain).DataOnly()
                    : new VaultContents { Accounts = JsonSerializer.Deserialize<List<OtpAccount>>(plain) ?? [] };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OtpAccount>> ImportAsync(
        string fileName,
        string masterPassword,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
        => (await ImportContentsAsync(fileName, masterPassword, provider, cancellationToken).ConfigureAwait(false)).Accounts;

    private static byte[] Aad(byte[] salt) => Encoding.UTF8.GetBytes($"MasterSafe.Backup.v{FullBackupVersion}|{Convert.ToBase64String(salt)}");
}
