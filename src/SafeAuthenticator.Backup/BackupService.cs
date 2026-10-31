// File:    BackupService.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.IO.Compression;
using System.Text.Json;
using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;

namespace SafeAuthenticator.Backup;

/// <summary>
/// Produces encrypted <c>.sauth</c> backups: accounts are GZip-compressed then AES-256-GCM encrypted
/// with an Argon2id-derived key. Plaintext is never written.
/// </summary>
public sealed class BackupService : IBackupService
{
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
        var compressed = Compress(json);
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
    public async Task<IReadOnlyList<OtpAccount>> ImportAsync(
        string fileName,
        string masterPassword,
        IBackupProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var bytes = await provider.DownloadAsync(fileName, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<VaultFile>(bytes)
            ?? throw new InvalidOperationException("Backup is corrupted or empty.");

        var salt = Convert.FromBase64String(envelope.Salt);
        var key = _masterPassword.DeriveKey(masterPassword, salt, envelope.KdfParameters);

        var payload = new EncryptedPayload(
            Convert.FromBase64String(envelope.Nonce),
            Convert.FromBase64String(envelope.Ciphertext),
            Convert.FromBase64String(envelope.Tag));

        var decrypted = _encryption.Decrypt(payload, key);
        var json = envelope.Compressed ? Decompress(decrypted) : decrypted;

        return JsonSerializer.Deserialize<List<OtpAccount>>(json) ?? [];
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
