// File:    VaultService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSafe.Core;

namespace MasterSafe.Crypto;

/// <summary>
/// Orchestrates key derivation, AES-256-GCM encryption and persistence.
/// Secrets are never written to disk in plaintext.
/// </summary>
/// <remarks>
/// Format 3 encrypts the vault contents with a random 256-bit data key. The data key is stored
/// once per unlock method (a "slot"), wrapped under a key that only that method can produce:
/// Argon2id of the master password, Windows Hello, a security key and so on. Changing the master
/// password or adding an unlock method therefore never re-encrypts the contents. Every encryption
/// binds the vault identifier (and, for slots, the slot identifier and type) as associated data,
/// so parts of one vault cannot be swapped into another. Formats 1 and 2 (a key derived directly
/// from the password) can still be opened and are upgraded to format 3 on the next password unlock.
/// </remarks>
public sealed class VaultService : IVaultService
{
    private const int CurrentVersion = 3;
    private const int DataKeySize = 32;

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
        _kdfParameters.Validate();
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
        if (vault.Version < CurrentVersion)
        {
            var legacyKey = DeriveLegacyKey(vault, masterPassword);
            try
            {
                return DecodeLegacy(vault, legacyKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(legacyKey);
            }
        }

        var dataKey = UnwrapWithPassword(vault, masterPassword);
        try
        {
            return DecodeCurrent(vault, dataKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> DeriveKeyAsync(string masterPassword, CancellationToken cancellationToken = default)
    {
        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (vault.Version >= CurrentVersion)
        {
            return UnwrapWithPassword(vault, masterPassword);
        }

        // Legacy vault: prove the password is right, then rewrite it in the current format.
        var legacyKey = DeriveLegacyKey(vault, masterPassword);
        VaultContents contents;
        try
        {
            contents = DecodeLegacy(vault, legacyKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(legacyKey);
        }

        return await CreateVaultAsync(masterPassword, contents, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VaultContents> OpenWithKeyAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        return vault.Version >= CurrentVersion ? DecodeCurrent(vault, key) : DecodeLegacy(vault, key);
    }

    /// <inheritdoc />
    public async Task SaveWithKeyAsync(byte[] key, VaultContents contents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(contents);

        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (vault.Version < CurrentVersion)
        {
            await WriteLegacyAsync(key, Convert.FromBase64String(vault.Salt), vault.KdfParameters, contents, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // A wrong or stale key must never overwrite the vault.
        CryptographicOperations.ZeroMemory(DecodeCurrentBytes(vault, key));
        await WriteCurrentAsync(vault.VaultId, vault.Slots, key, contents, cancellationToken).ConfigureAwait(false);
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
            catch (CryptographicException)
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

        var existing = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is { Version: >= CurrentVersion })
        {
            // Existing vault: keep its data key and unlock methods, but only for the right password.
            var dataKey = UnwrapWithPassword(existing, masterPassword);
            try
            {
                await WriteCurrentAsync(existing.VaultId, existing.Slots, dataKey, contents, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }

            return;
        }

        var newKey = await CreateVaultAsync(masterPassword, contents, cancellationToken).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(newKey);
    }

    /// <inheritdoc />
    public async Task RotateMasterPasswordAsync(string currentPassword, string newPassword, Argon2Parameters? kdf = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        kdf?.Validate();

        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (vault.Version < CurrentVersion)
        {
            var contents = await OpenContentsAsync(currentPassword, cancellationToken).ConfigureAwait(false);
            var created = await CreateVaultAsync(newPassword, contents, cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(created);
            await _repository.ForgetOldCopiesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var dataKey = UnwrapWithPassword(vault, currentPassword);
        try
        {
            var passwordSlot = FindSlot(vault, SlotTypes.Password);
            var replacement = CreatePasswordSlot(vault.VaultId, newPassword, dataKey, kdf ?? _kdfParameters);
            var slots = vault.Slots.Select(s => s.Id == passwordSlot.Id ? replacement : s).ToList();
            await WriteCurrentAsync(vault.VaultId, slots, dataKey, DecodeCurrent(vault, dataKey), cancellationToken).ConfigureAwait(false);
            await _repository.ForgetOldCopiesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeySlotInfo>> ListSlotsAsync(CancellationToken cancellationToken = default)
    {
        var vault = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (vault is null || vault.Version < CurrentVersion)
        {
            return [];
        }

        return vault.Slots.Select(s => new KeySlotInfo(s.Id, s.Type, s.Label, s.CreatedUtc, s.Kdf)).ToList();
    }

    /// <inheritdoc />
    public async Task AddSlotAsync(byte[] dataKey, ISecretProvider provider, string label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(provider);

        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (vault.Version < CurrentVersion)
        {
            throw new InvalidOperationException("Unlock the vault with the master password once to upgrade it first.");
        }

        CryptographicOperations.ZeroMemory(DecodeCurrentBytes(vault, dataKey));

        // Only one slot per hardware method: enrolling again replaces the old one.
        var existing = vault.Slots.FirstOrDefault(s => s.Type == provider.SlotType);
        var slotId = NewId();
        var enrollment = await provider.EnrollAsync(slotId, cancellationToken).ConfigureAwait(false);
        try
        {
            var wrapped = _encryption.Encrypt(dataKey, enrollment.Key, SlotAad(vault.VaultId, slotId, provider.SlotType));
            var slot = new KeySlot
            {
                Id = slotId,
                Type = provider.SlotType,
                Label = string.IsNullOrWhiteSpace(label) ? provider.DisplayName : label,
                CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
                Salt = Convert.ToBase64String(enrollment.Data),
                Nonce = Convert.ToBase64String(wrapped.Nonce),
                Tag = Convert.ToBase64String(wrapped.Tag),
                WrappedKey = Convert.ToBase64String(wrapped.Ciphertext),
            };

            var slots = vault.Slots.Where(s => s.Id != existing?.Id).Append(slot).ToList();
            await _repository.SaveAsync(CloneWithSlots(vault, slots), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(enrollment.Key);
        }

        if (existing is not null)
        {
            await provider.RemoveAsync(existing.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> UnlockWithProviderAsync(ISecretProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        var slot = vault.Version >= CurrentVersion ? vault.Slots.FirstOrDefault(s => s.Type == provider.SlotType) : null;
        if (slot is null)
        {
            throw new InvalidOperationException($"{provider.DisplayName} is not set up for this vault.");
        }

        var key = await provider.DeriveAsync(slot.Id, Convert.FromBase64String(slot.Salt), cancellationToken).ConfigureAwait(false);
        try
        {
            return UnwrapSlot(vault.VaultId, slot, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveSlotAsync(byte[] dataKey, string slotId, ISecretProvider? provider = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataKey);

        var vault = await LoadRequiredAsync(cancellationToken).ConfigureAwait(false);
        var slot = vault.Slots.FirstOrDefault(s => s.Id == slotId)
            ?? throw new InvalidOperationException("That unlock method does not exist.");

        if (slot.Type == SlotTypes.Password)
        {
            throw new InvalidOperationException("The master password cannot be removed.");
        }

        CryptographicOperations.ZeroMemory(DecodeCurrentBytes(vault, dataKey));
        await _repository.SaveAsync(CloneWithSlots(vault, vault.Slots.Where(s => s.Id != slotId).ToList()), cancellationToken)
            .ConfigureAwait(false);
        await _repository.ForgetOldCopiesAsync(cancellationToken).ConfigureAwait(false);

        if (provider is not null)
        {
            await provider.RemoveAsync(slotId, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- format 3

    private async Task<byte[]> CreateVaultAsync(string masterPassword, VaultContents contents, CancellationToken cancellationToken)
    {
        var dataKey = RandomNumberGenerator.GetBytes(DataKeySize);
        try
        {
            var vaultId = NewId();
            var slot = CreatePasswordSlot(vaultId, masterPassword, dataKey, _kdfParameters);
            await WriteCurrentAsync(vaultId, [slot], dataKey, contents, cancellationToken).ConfigureAwait(false);
            return dataKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
    }

    private KeySlot CreatePasswordSlot(string vaultId, string masterPassword, byte[] dataKey, Argon2Parameters kdf)
    {
        var salt = _masterPassword.GenerateSalt();
        var kek = _masterPassword.DeriveKey(masterPassword, salt, kdf);
        try
        {
            var id = NewId();
            var wrapped = _encryption.Encrypt(dataKey, kek, SlotAad(vaultId, id, SlotTypes.Password));
            return new KeySlot
            {
                Id = id,
                Type = SlotTypes.Password,
                Label = "Master password",
                CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
                Salt = Convert.ToBase64String(salt),
                Kdf = kdf,
                Nonce = Convert.ToBase64String(wrapped.Nonce),
                Tag = Convert.ToBase64String(wrapped.Tag),
                WrappedKey = Convert.ToBase64String(wrapped.Ciphertext),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private byte[] UnwrapWithPassword(VaultFile vault, string masterPassword)
    {
        var slot = FindSlot(vault, SlotTypes.Password);
        var kdf = slot.Kdf ?? throw new InvalidDataException("The password slot has no key-derivation parameters.");
        kdf.Validate();

        var kek = _masterPassword.DeriveKey(masterPassword, Convert.FromBase64String(slot.Salt), kdf);
        try
        {
            return UnwrapSlot(vault.VaultId, slot, kek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private byte[] UnwrapSlot(string vaultId, KeySlot slot, byte[] kek)
    {
        var payload = new EncryptedPayload(
            Convert.FromBase64String(slot.Nonce),
            Convert.FromBase64String(slot.WrappedKey),
            Convert.FromBase64String(slot.Tag));

        var key = _encryption.Decrypt(payload, kek, SlotAad(vaultId, slot.Id, slot.Type));
        if (key.Length != DataKeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("The stored key has the wrong length.");
        }

        return key;
    }

    private static KeySlot FindSlot(VaultFile vault, string type)
        => vault.Slots.FirstOrDefault(s => s.Type == type)
            ?? throw new InvalidDataException("The vault has no master-password slot.");

    private VaultContents DecodeCurrent(VaultFile vault, byte[] dataKey)
    {
        var json = DecodeCurrentBytes(vault, dataKey);
        try
        {
            return VaultContentsCodec.Decode(json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private byte[] DecodeCurrentBytes(VaultFile vault, byte[] dataKey)
    {
        var payload = new EncryptedPayload(
            Convert.FromBase64String(vault.Nonce),
            Convert.FromBase64String(vault.Ciphertext),
            Convert.FromBase64String(vault.Tag));

        var decrypted = _encryption.Decrypt(payload, dataKey, ContentAad(vault.VaultId));
        try
        {
            return BoundedGzip.Decompress(decrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
        }
    }

    private async Task WriteCurrentAsync(
        string vaultId, IReadOnlyList<KeySlot> slots, byte[] dataKey, VaultContents contents, CancellationToken cancellationToken)
    {
        var json = VaultContentsCodec.Encode(contents);
        var compressed = BoundedGzip.Compress(json);
        CryptographicOperations.ZeroMemory(json);

        EncryptedPayload payload;
        try
        {
            payload = _encryption.Encrypt(compressed, dataKey, ContentAad(vaultId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compressed);
        }

        var vault = new VaultFile
        {
            Version = CurrentVersion,
            VaultId = vaultId,
            Slots = slots,
            Nonce = Convert.ToBase64String(payload.Nonce),
            Tag = Convert.ToBase64String(payload.Tag),
            Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            Compressed = true,
        };

        await _repository.SaveAsync(vault, cancellationToken).ConfigureAwait(false);
    }

    private static VaultFile CloneWithSlots(VaultFile vault, IReadOnlyList<KeySlot> slots) => new()
    {
        Version = vault.Version,
        VaultId = vault.VaultId,
        Slots = slots,
        Nonce = vault.Nonce,
        Tag = vault.Tag,
        Ciphertext = vault.Ciphertext,
        Compressed = vault.Compressed,
    };

    private static byte[] ContentAad(string vaultId)
        => Encoding.UTF8.GetBytes($"MasterSafe.Vault.Content|v{CurrentVersion}|{vaultId}");

    private static byte[] SlotAad(string vaultId, string slotId, string type)
        => Encoding.UTF8.GetBytes($"MasterSafe.Vault.Slot|v{CurrentVersion}|{vaultId}|{slotId}|{type}");

    private static string NewId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private async Task<VaultFile> LoadRequiredAsync(CancellationToken cancellationToken)
        => await _repository.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No vault exists. Call CreateAsync first.");

    // ------------------------------------------------------- legacy formats 1 and 2

    private byte[] DeriveLegacyKey(VaultFile vault, string masterPassword)
    {
        vault.KdfParameters.Validate();
        return _masterPassword.DeriveKey(masterPassword, Convert.FromBase64String(vault.Salt), vault.KdfParameters);
    }

    private VaultContents DecodeLegacy(VaultFile vault, byte[] key)
    {
        var payload = new EncryptedPayload(
            Convert.FromBase64String(vault.Nonce),
            Convert.FromBase64String(vault.Ciphertext),
            Convert.FromBase64String(vault.Tag));

        var decrypted = _encryption.Decrypt(payload, key);
        var json = vault.Compressed ? BoundedGzip.Decompress(decrypted) : decrypted;

        if (vault.Version >= 2)
        {
            return JsonSerializer.Deserialize<VaultContents>(json) ?? new VaultContents();
        }

        // Format v1: a bare JSON array of OTP accounts.
        var accounts = JsonSerializer.Deserialize<List<OtpAccount>>(json) ?? [];
        return new VaultContents { Accounts = accounts };
    }

    private async Task WriteLegacyAsync(byte[] key, byte[] salt, Argon2Parameters kdf, VaultContents contents, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(contents);
        var payload = _encryption.Encrypt(BoundedGzip.Compress(json), key);

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
}
