// File:    PasswordHealth.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>A password entry with the reason it needs attention.</summary>
public sealed record HealthIssue(PasswordEntry Entry, string Reason);

/// <summary>What the health check found.</summary>
public sealed class PasswordHealthReport
{
    /// <summary>Passwords that are easy to guess.</summary>
    public IReadOnlyList<HealthIssue> Weak { get; init; } = [];

    /// <summary>Passwords used by more than one entry.</summary>
    public IReadOnlyList<HealthIssue> Reused { get; init; } = [];

    /// <summary>Passwords that have not been changed for a long time.</summary>
    public IReadOnlyList<HealthIssue> Old { get; init; } = [];

    /// <summary>Passwords found in known data breaches (only filled when the optional check was run).</summary>
    public IReadOnlyList<HealthIssue> Breached { get; init; } = [];

    /// <summary>Entries that look like copies of each other (same address and user name, or same title and user name).</summary>
    public IReadOnlyList<HealthIssue> Duplicates { get; init; } = [];

    /// <summary>Sign-ins on an http:// address, which is not encrypted.</summary>
    public IReadOnlyList<HealthIssue> Insecure { get; init; } = [];

    /// <summary>Entries past the date the user set, or close to it.</summary>
    public IReadOnlyList<HealthIssue> Expiring { get; init; } = [];

    /// <summary>How many entries were looked at.</summary>
    public int Checked { get; init; }

    /// <summary>True when nothing needs attention.</summary>
    public bool IsClean => EntriesNeedingAttention == 0;

    /// <summary>How many different entries need attention.</summary>
    public int EntriesNeedingAttention => Weak.Concat(Reused).Concat(Old).Concat(Breached).Concat(Duplicates).Concat(Insecure).Concat(Expiring)
        .Select(i => i.Entry.Id).Distinct().Count();
}

/// <summary>Looks through the passwords for ones that are weak, reused, old or known from breaches. Everything runs offline.</summary>
public static class PasswordHealth
{
    /// <summary>Passwords older than this many days are listed as old.</summary>
    public const int OldAfterDays = 365;

    /// <summary>An entry is listed this many days before its expiry date.</summary>
    public const int ExpiryWarningDays = 30;

    /// <summary>Builds the report for the entries that are in use.</summary>
    /// <param name="entries">All entries; those in the recycle bin are ignored.</param>
    /// <param name="now">The current time.</param>
    /// <param name="breachCounts">How often a password was seen in breaches, by entry id, when the optional check was run.</param>
    public static PasswordHealthReport Analyze(IEnumerable<PasswordEntry> entries, DateTimeOffset now, IReadOnlyDictionary<Guid, int>? breachCounts = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var inUse = entries.Where(e => !e.IsDeleted).ToList();
        var active = inUse.Where(e => !string.IsNullOrEmpty(e.Password)).ToList();

        var weak = active
            .Select(e => (Entry: e, Strength: PasswordStrength.Evaluate(e.Password, e.Title, e.UserName)))
            .Where(x => x.Strength.Score <= 1)
            .Select(x => new HealthIssue(x.Entry, $"{x.Strength.Label}. {x.Strength.Hint}".Trim()))
            .ToList();

        var reused = active
            .GroupBy(e => e.Password, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(e => new HealthIssue(e, $"The same password is used by {g.Count() - 1} other {(g.Count() == 2 ? "entry" : "entries")}.")))
            .ToList();

        var old = active
            .Where(e => now - e.ModifiedUtc > TimeSpan.FromDays(OldAfterDays))
            .Select(e => new HealthIssue(e, $"Not changed for {(int)((now - e.ModifiedUtc).TotalDays / 30)} months."))
            .OrderByDescending(i => now - i.Entry.ModifiedUtc)
            .ToList();

        var breached = breachCounts is null
            ? []
            : active
                .Where(e => breachCounts.TryGetValue(e.Id, out var n) && n > 0)
                .Select(e => new HealthIssue(e, $"Found in known data breaches ({breachCounts[e.Id].ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} times). Change it."))
                .ToList();

        var insecure = active
            .Where(IsInsecure)
            .Select(e => new HealthIssue(e, "This address is not encrypted (http). If the site supports it, use https."))
            .ToList();

        var expiring = inUse
            .Where(e => ExpiresSoon(e, now))
            .OrderBy(e => e.ExpiresUtc)
            .Select(e => new HealthIssue(e, DescribeExpiry(e.ExpiresUtc!.Value, now)))
            .ToList();

        return new PasswordHealthReport
        {
            Weak = weak,
            Reused = reused,
            Old = old,
            Breached = breached,
            Duplicates = FindDuplicates(inUse),
            Insecure = insecure,
            Expiring = expiring,
            Checked = active.Count,
        };
    }

    /// <summary>How many entries are past their date or close to it, for the reminder on the codes page.</summary>
    public static int CountExpiring(IEnumerable<PasswordEntry> entries, DateTimeOffset now)
        => entries.Count(e => !e.IsDeleted && ExpiresSoon(e, now));

    /// <summary>The same address with https, or null when it does not start with http://.</summary>
    public static string? HttpsVersion(string url)
    {
        var trimmed = url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + trimmed["http://".Length..] : null;
    }

    private static bool IsInsecure(PasswordEntry entry) => HttpsVersion(entry.Url) is not null;

    private static bool ExpiresSoon(PasswordEntry entry, DateTimeOffset now)
        => entry.ExpiresUtc is { } expires && expires - now <= TimeSpan.FromDays(ExpiryWarningDays);

    private static string DescribeExpiry(DateTimeOffset expires, DateTimeOffset now)
    {
        var days = (int)Math.Ceiling((expires - now).TotalDays);
        return days switch
        {
            < 0 => $"Expired {-days} days ago. Change it, then set a new date.",
            0 => "Expires today. Change it, then set a new date.",
            1 => "Expires tomorrow.",
            _ => $"Expires in {days} days.",
        };
    }

    private static List<HealthIssue> FindDuplicates(List<PasswordEntry> entries)
    {
        static string Clean(string text) => text.Trim().ToLowerInvariant();
        static string Address(string url) => Clean(url).TrimEnd('/').Replace("https://", string.Empty, StringComparison.Ordinal).Replace("http://", string.Empty, StringComparison.Ordinal);

        // Same address and user name, or, with no address, same title and user name.
        return entries
            .Where(e => e.Kind == EntryKind.Login)
            .GroupBy(e => e.Url.Trim().Length > 0 ? $"u|{Address(e.Url)}|{Clean(e.UserName)}" : $"t|{Clean(e.Title)}|{Clean(e.UserName)}", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(e => new HealthIssue(e, g.Count() == 2 ? "Looks like a copy of another entry." : $"Looks like a copy of {g.Count() - 1} other entries.")))
            .ToList();
    }
}
