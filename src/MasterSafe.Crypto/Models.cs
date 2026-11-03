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
    /// <summary>Largest memory cost accepted from a file (1 GiB). Protects against crafted vaults.</summary>
    public const int MaxMemorySizeKib = 1_048_576;

    /// <summary>Largest time cost accepted from a file.</summary>
    public const int MaxIterations = 20;

    /// <summary>Largest parallelism accepted from a file.</summary>
    public const int MaxParallelism = 32;

    /// <summary>Memory cost in kibibytes. Default 128 MiB.</summary>
    public int MemorySizeKib { get; init; } = 131072;

    /// <summary>Number of iterations (time cost). Default 3.</summary>
    public int Iterations { get; init; } = 3;

    /// <summary>Degree of parallelism. Default 4.</summary>
    public int DegreeOfParallelism { get; init; } = 4;

    /// <summary>Derived key length in bytes. Default 32 (AES-256).</summary>
    public int KeyLength { get; init; } = 32;

    /// <summary>Default parameters (above the OWASP minimum for Argon2id).</summary>
    public static Argon2Parameters Default { get; } = new();

    /// <summary>
    /// Rejects values that are invalid for Argon2id or large enough to exhaust memory or CPU.
    /// Parameters read from a vault or backup file are untrusted, so they are checked before use.
    /// </summary>
    /// <exception cref="InvalidDataException">A value is outside the accepted range.</exception>
    public void Validate()
    {
        if (DegreeOfParallelism is < 1 or > MaxParallelism
            || Iterations is < 1 or > MaxIterations
            || MemorySizeKib < 8 * DegreeOfParallelism
            || MemorySizeKib > MaxMemorySizeKib
            || KeyLength != 32)
        {
            throw new InvalidDataException("The key-derivation parameters in this file are outside the accepted range.");
        }
    }
}

/// <summary>Well-known <see cref="KeySlot.Type"/> values.</summary>
public static class SlotTypes
{
    /// <summary>The master password (Argon2id).</summary>
    public const string Password = "password";

    /// <summary>A Windows Hello credential.</summary>
    public const string WindowsHello = "windows-hello";

    /// <summary>A FIDO2 security key using the hmac-secret extension.</summary>
    public const string Fido2 = "fido2-hmac-secret";
}

/// <summary>
/// One way to unlock the vault. Each slot stores the vault's random data key, encrypted
/// under a key that only that unlock method can produce.
/// </summary>
public sealed class KeySlot
{
    /// <summary>Random slot identifier (Base64).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>One of <see cref="SlotTypes"/>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Name shown to the user, for example "Windows Hello" or "YubiKey".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>UTC creation time (ISO 8601).</summary>
    public string CreatedUtc { get; init; } = string.Empty;

    /// <summary>Password slots: Argon2 salt. Hardware slots: provider enrollment data (Base64).</summary>
    public string Salt { get; init; } = string.Empty;

    /// <summary>Password slots only: Argon2 parameters.</summary>
    public Argon2Parameters? Kdf { get; init; }

    /// <summary>AES-GCM nonce of the wrapped data key (Base64).</summary>
    public string Nonce { get; init; } = string.Empty;

    /// <summary>AES-GCM tag of the wrapped data key (Base64).</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The wrapped data key (Base64).</summary>
    public string WrappedKey { get; init; } = string.Empty;
}

/// <summary>Display information about a <see cref="KeySlot"/>.</summary>
public sealed record KeySlotInfo(string Id, string Type, string Label, string CreatedUtc, Argon2Parameters? Kdf = null);

/// <summary>
/// Result of an AES-256-GCM encryption operation.
/// </summary>
public sealed record EncryptedPayload(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>
/// Serializable on-disk representation of an encrypted vault.
/// </summary>
public sealed class VaultFile
{
    /// <summary>File format version. 1 and 2 are read-only legacy formats; 3 uses key slots.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Format 3: random vault identifier (Base64), bound into every encryption as associated data.</summary>
    public string VaultId { get; init; } = string.Empty;

    /// <summary>Format 3: the unlock methods. Each wraps the same random data key.</summary>
    public IReadOnlyList<KeySlot> Slots { get; init; } = [];

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
/// The security settings of a vault. They live inside the encrypted vault because the settings file is not protected:
/// a program that can edit that file must not be able to turn these off. After unlocking, these values win.
/// </summary>
public sealed record VaultSecurity
{
    /// <summary>Minutes without activity before the vault locks (1 to 120).</summary>
    public int AutoLockMinutes { get; init; } = 5;

    /// <summary>Lock when the computer locks or goes to sleep.</summary>
    public bool LockOnSessionLock { get; init; } = true;

    /// <summary>Lock when the window is minimized.</summary>
    public bool LockOnMinimize { get; init; }

    /// <summary>Seconds a copied secret stays on the clipboard.</summary>
    public int ClipboardClearSeconds { get; init; } = 120;

    /// <summary>Keep the window out of screenshots and screen sharing.</summary>
    public bool BlockScreenCapture { get; init; } = true;

    /// <summary>The breach check may be used.</summary>
    public bool BreachCheckEnabled { get; init; }

    /// <summary>Codes stay hidden until they are clicked.</summary>
    public bool HideCodes { get; init; }

    /// <summary>Revealed passwords hide again when the window loses focus.</summary>
    public bool HideRevealedOnFocusLoss { get; init; } = true;

    /// <summary>The "use the site's icon" button may be used.</summary>
    public bool WebsiteIconsEnabled { get; init; }
}

/// <summary>An item deleted for good, remembered so that sync does not bring it back from another computer.</summary>
public sealed record Tombstone
{
    /// <summary>How many are kept.</summary>
    public const int MaxKept = 5000;

    /// <summary>The id of the entry or account.</summary>
    public Guid Id { get; init; }

    /// <summary>When it was deleted.</summary>
    public DateTimeOffset DeletedUtc { get; init; }
}

/// <summary>How this vault syncs with other computers. Kept inside the vault because it holds a key and a server password.</summary>
public sealed record SyncSettings
{
    /// <summary>True for a WebDAV server, false for a folder.</summary>
    public bool UseWebDav { get; init; }

    /// <summary>The folder sync files go to.</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>The WebDAV folder address.</summary>
    public string WebDavUrl { get; init; } = string.Empty;

    /// <summary>The WebDAV user name.</summary>
    public string WebDavUser { get; init; } = string.Empty;

    /// <summary>The WebDAV password.</summary>
    public string WebDavPassword { get; init; } = string.Empty;

    /// <summary>The random key that encrypts sync files (Base64). It is not derived from any password.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Sync after unlocking and a short time after a change.</summary>
    public bool Automatic { get; init; }

    /// <summary>The sync files already merged here.</summary>
    public IReadOnlyList<string> SeenFiles { get; init; } = [];

    /// <summary>Fingerprint of the state after the last sync, so nothing is written when nothing changed.</summary>
    public string? LastStateHash { get; init; }

    /// <summary>When the last sync finished.</summary>
    public DateTimeOffset? LastSyncUtc { get; init; }
}

/// <summary>
/// Decrypted vault contents (payload format v2): OTP accounts plus KeePass-style password entries.
/// Format v1 stored only a JSON array of OTP accounts; it is still readable. Every property added later is optional, and
/// a version that does not know it simply leaves it out, so it must be safe to lose.
/// </summary>
public sealed record VaultContents
{
    /// <summary>OTP accounts.</summary>
    public IReadOnlyList<MasterSafe.Core.OtpAccount> Accounts { get; init; } = [];

    /// <summary>Stored password entries.</summary>
    public IReadOnlyList<MasterSafe.Core.PasswordEntry> Passwords { get; init; } = [];

    /// <summary>Password folders that have no entries yet (folders with entries are known from the entries).</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>The icon chosen for a folder: folder path to icon name. Folders without an entry here use the default icon.</summary>
    public IReadOnlyDictionary<string, string> FolderIcons { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The fingerprint of the recovery key this vault trusts. It lives here, encrypted, because the settings file is not
    /// protected: someone who could swap the key there would receive every later recovery backup.
    /// </summary>
    public string? RecoveryKeyFingerprint { get; init; }

    /// <summary>The security settings of this vault. Null until they are first saved, for a vault from before 1.6.</summary>
    public VaultSecurity? Security { get; init; }

    /// <summary>
    /// Goes up with every save. A copy of the number is kept outside the vault file, so an older copy of the vault can be
    /// noticed. 0 means the vault has none yet.
    /// </summary>
    public long SaveCounter { get; init; }

    /// <summary>Items deleted for good.</summary>
    public IReadOnlyList<Tombstone> Tombstones { get; init; } = [];

    /// <summary>How this vault syncs. Null when it does not.</summary>
    public SyncSettings? Sync { get; init; }

    /// <summary>
    /// The bytes of the files attached to password entries, by attachment id. They are not part of the JSON; they are
    /// stored in their own section (see <see cref="VaultContentsCodec"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<Guid, byte[]> AttachmentData { get; init; } = new Dictionary<Guid, byte[]>();

    /// <summary>The entries and accounts alone, without anything that belongs to this computer or this vault file.</summary>
    public VaultContents DataOnly() => this with { Sync = null, Security = null, SaveCounter = 0, RecoveryKeyFingerprint = null };
}
