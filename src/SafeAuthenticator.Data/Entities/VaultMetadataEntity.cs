using System.ComponentModel.DataAnnotations;

namespace SafeAuthenticator.Data.Entities;

/// <summary>
/// Single-row vault metadata (KDF salt, parameters, schema version).
/// </summary>
public sealed class VaultMetadataEntity
{
    [Key]
    public int Id { get; set; } = 1;

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Base64 Argon2id salt.</summary>
    [MaxLength(512)]
    public string Salt { get; set; } = string.Empty;

    public int KdfMemoryKib { get; set; } = 65536;

    public int KdfIterations { get; set; } = 3;

    public int KdfParallelism { get; set; } = 4;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
