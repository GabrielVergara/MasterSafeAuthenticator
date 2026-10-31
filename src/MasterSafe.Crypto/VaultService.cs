// File:    VaultService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.IO.Compression;
using System.Text.Json;
using MasterSafe.Core;

namespace MasterSafe.Crypto;

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
        => SaveContentsAsync(masterPassword, new VaultContents(), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OtpAccount>> OpenAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        var contents = await OpenContentsAsync(masterPassword, cancellationToken).ConfigureAwait(false);
        return contents.Accounts;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        => _repository.ExistsAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<VaultContents> OpenContentsAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        var key = _masterPassword.DeriveKey(masterPassword, Convert.FromBase64String(vault.Salt), vault.KdfParameters);
        return Decode(vault, key);
    }

    /// <inheritdoc />
    public async Task<byte[]> DeriveKeyAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        var key = _masterPassword.DeriveKey(masterPassword, Convert.FromBase64String(vault.Salt), vault.KdfParameters);

        // Verify the password is correct before handing the key out.
        Decode(vault, key);
        return key;
    }

    /// <inheritdoc />
    public async Task<VaultContents> OpenWithKeyAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        return Decode(vault, key);
    }

    /// <inheritdoc />
    public async Task SaveWithKeyAsync(byte[] key, VaultContents contents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(contents);

        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(key, Convert.FromBase64String(vault.Salt), vault.KdfParameters, contents, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string masterPassword, IEnumerable<OtpAccount> accounts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        // Preserve any stored passwords when only accounts are being saved.
        IReadOnlyList<PasswordEntry> passwords = [];
        if (await _repository.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                passwords = (await OpenContentsAsync(masterPassword, cancellationToken).ConfigureAwait(false)).Passwords;
            }
            catch (InvalidPasswordException)
            {
                throw;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidPasswordException();
            }
        }

        await SaveContentsAsync(
            masterPassword,
            new VaultContents { Accounts = accounts.ToList(), Passwords = passwords },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveContentsAsync(string masterPassword, VaultContents contents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var salt = _masterPassword.GenerateSalt();
        var key = _masterPassword.DeriveKey(masterPassword, salt, _kdfParameters);
        await WriteAsync(key, salt, _kdfParameters, contents, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RotateMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var contents = await OpenContentsAsync(currentPassword, cancellationToken).ConfigureAwait(false);
        await SaveContentsAsync(newPassword, contents, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VaultFile> LoadRequiredAsync(CancellationToken cancellationToken)
        => await _repository.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No vault exists. Call CreateAsync first.");

    private VaultContents Decode(VaultFile vault, byte[] key)
    {
        var payload = new EncryptedPayload(
            Convert.FromBase64String(vault.Nonce),
            Convert.FromBase64String(vault.Ciphertext),
            Convert.FromBase64String(vault.Tag));

        var decrypted = _encryption.Decrypt(payload, key);
        var json = vault.Compressed ? Decompress(decrypted) : decrypted;

        if (vault.Version >= 2)
        {
            return JsonSerializer.Deserialize<VaultContents>(json) ?? new VaultContents();
        }

        // Format v1: a bare JSON array of OTP accounts.
        var accounts = JsonSerializer.Deserialize<List<OtpAccount>>(json) ?? [];
        return new VaultContents { Accounts = accounts };
    }

    private async Task WriteAsync(byte[] key, byte[] salt, Argon2Parameters kdf, VaultContents contents, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(contents);
        var payload = _encryption.Encrypt(Compress(json), key);

        var vault = new VaultFile
        {
            Version = 2,
            Salt = Convert.ToBase64String(salt),
            KdfParameters = kdf,
            Nonce = Convert.ToBase64String(payload.Nonce),
            Tag = Convert.ToBase64String(payload.Tag),
            Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            Compressed = true,
        };

        await _repository.SaveAsync(vault, cancellationToken).ConfigureAwait(false);
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
