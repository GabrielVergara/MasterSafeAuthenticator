using System.IO.Compression;
using System.Text.Json;
using SafeAuthenticator.Core;

namespace SafeAuthenticator.Crypto;

/// <summary>
/// Orchestrates master-password derivation, AES-256-GCM encryption and persistence.
/// Secrets are never written to disk in plaintext.
/// </summary>
public sealed class VaultService : IVaultService
{
    private readonly IVaultRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly IMasterPasswordService _masterPassword;
    private readonly Argon2Parameters _kdfParameters;

    public VaultService(
        IVaultRepository repository,
        IEncryptionService encryption,
        IMasterPasswordService masterPassword,
        Argon2Parameters? kdfParameters = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _masterPassword = masterPassword ?? throw new ArgumentNullException(nameof(masterPassword));
        _kdfParameters = kdfParameters ?? Argon2Parameters.Default;
    }

    /// <inheritdoc />
    public Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default)
        => SaveAsync(masterPassword, [], cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OtpAccount>> OpenAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        var vault = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No vault exists. Call CreateAsync first.");

        var salt = Convert.FromBase64String(vault.Salt);
        var key = _masterPassword.DeriveKey(masterPassword, salt, vault.KdfParameters);

        var payload = new EncryptedPayload(
            Convert.FromBase64String(vault.Nonce),
            Convert.FromBase64String(vault.Ciphertext),
            Convert.FromBase64String(vault.Tag));

        var decrypted = _encryption.Decrypt(payload, key);
        var json = vault.Compressed ? Decompress(decrypted) : decrypted;

        var accounts = JsonSerializer.Deserialize<List<OtpAccount>>(json) ?? [];
        return accounts;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string masterPassword, IEnumerable<OtpAccount> accounts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var salt = _masterPassword.GenerateSalt();
        var key = _masterPassword.DeriveKey(masterPassword, salt, _kdfParameters);

        var json = JsonSerializer.SerializeToUtf8Bytes(accounts.ToList());
        var plaintext = Compress(json);

        var payload = _encryption.Encrypt(plaintext, key);

        var vault = new VaultFile
        {
            Version = 1,
            Salt = Convert.ToBase64String(salt),
            KdfParameters = _kdfParameters,
            Nonce = Convert.ToBase64String(payload.Nonce),
            Tag = Convert.ToBase64String(payload.Tag),
            Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            Compressed = true,
        };

        await _repository.SaveAsync(vault, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RotateMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var accounts = await OpenAsync(currentPassword, cancellationToken).ConfigureAwait(false);
        await SaveAsync(newPassword, accounts, cancellationToken).ConfigureAwait(false);
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
