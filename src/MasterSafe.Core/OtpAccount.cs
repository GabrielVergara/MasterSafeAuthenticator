// File:    OtpAccount.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>
/// Represents a single OTP account (issuer + label + secret + parameters).
/// </summary>
public sealed class OtpAccount
{
    /// <summary>Stable identifier for persistence.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Human-readable issuer/service name (e.g. "GitHub").</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Account label, usually the user name or email.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Base32-encoded shared secret.</summary>
    public required string Secret { get; init; }

    /// <summary>TOTP or HOTP.</summary>
    public OtpType Type { get; init; } = OtpType.Totp;

    /// <summary>Hash algorithm.</summary>
    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;

    /// <summary>Number of digits (6 or 8).</summary>
    public int Digits { get; init; } = 6;

    /// <summary>TOTP time-step in seconds.</summary>
    public int Period { get; init; } = 30;

    /// <summary>HOTP counter (ignored for TOTP).</summary>
    public long Counter { get; init; }

    /// <summary>Optional tags for grouping.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The group the account is shown in, for example "Work". Empty means no group.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>When the names or the group last changed. Empty for accounts from older versions. Sync uses it to find the newer copy.</summary>
    public DateTimeOffset? ModifiedUtc { get; init; }

    /// <summary>Returns a copy with other names and group. The secret and the settings stay the same.</summary>
    public OtpAccount Renamed(string issuer, string label, string group, DateTimeOffset when)
        => Copy(issuer, label, group, when, Counter);

    /// <summary>Returns a copy with another counter (one-time passwords counted by use).</summary>
    public OtpAccount WithCounter(long counter) => Copy(Issuer, Label, Group, ModifiedUtc, counter);

    private OtpAccount Copy(string issuer, string label, string group, DateTimeOffset? modified, long counter) => new()
    {
        Id = Id,
        Issuer = issuer,
        Label = label,
        Group = group,
        ModifiedUtc = modified,
        Secret = Secret,
        Type = Type,
        Algorithm = Algorithm,
        Digits = Digits,
        Period = Period,
        Counter = counter,
        Tags = Tags,
    };

    /// <summary>
    /// Validates the account's secret and parameters.
    /// </summary>
    public bool IsValid(out string? error)
    {
        if (!Base32.IsValid(Secret))
        {
            error = "Secret is not valid Base32.";
            return false;
        }

        if (Digits is < 6 or > 8)
        {
            error = "Digits must be 6, 7 or 8.";
            return false;
        }

        if (Period <= 0)
        {
            error = "Period must be positive.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Generates the current OTP code for this account at the supplied time.
    /// </summary>
    public string GenerateCode(DateTimeOffset now)
    {
        var secretBytes = Base32.Decode(Secret);
        return Type == OtpType.Hotp
            ? HotpGenerator.Generate(secretBytes, Counter, Digits, Algorithm)
            : TotpGenerator.Generate(secretBytes, now, Period, Digits, Algorithm);
    }
}
