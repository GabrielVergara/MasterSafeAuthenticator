// File:    AccountEntity.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.ComponentModel.DataAnnotations;

namespace SafeAuthenticator.Data.Entities;

/// <summary>
/// Persisted account row. The <see cref="EncryptedSecret"/> column never contains a plaintext secret.
/// </summary>
public sealed class AccountEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Issuer { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Ciphertext of the OTP shared secret (never plaintext).</summary>
    [Required]
    public byte[] EncryptedSecret { get; set; } = [];

    public string Type { get; set; } = "Totp";

    public string Algorithm { get; set; } = "Sha1";

    public int Digits { get; set; } = 6;

    public int Period { get; set; } = 30;

    public long Counter { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TagEntity> Tags { get; set; } = [];
}
