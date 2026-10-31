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

    /// <summary>How many entries were looked at.</summary>
    public int Checked { get; init; }

    /// <summary>True when nothing needs attention.</summary>
    public bool IsClean => Weak.Count == 0 && Reused.Count == 0 && Old.Count == 0 && Breached.Count == 0;

    /// <summary>How many different entries need attention.</summary>
    public int EntriesNeedingAttention => Weak.Concat(Reused).Concat(Old).Concat(Breached).Select(i => i.Entry.Id).Distinct().Count();
}

/// <summary>Looks through the passwords for ones that are weak, reused, old or known from breaches. Everything runs offline.</summary>
public static class PasswordHealth
{
    /// <summary>Passwords older than this many days are listed as old.</summary>
    public const int OldAfterDays = 365;

    /// <summary>Builds the report for the entries that are in use.</summary>
    /// <param name="entries">All entries; those in the recycle bin are ignored.</param>
    /// <param name="now">The current time.</param>
    /// <param name="breachCounts">How often a password was seen in breaches, by entry id, when the optional check was run.</param>
    public static PasswordHealthReport Analyze(IEnumerable<PasswordEntry> entries, DateTimeOffset now, IReadOnlyDictionary<Guid, int>? breachCounts = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var active = entries.Where(e => !e.IsDeleted && !string.IsNullOrEmpty(e.Password)).ToList();

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

        return new PasswordHealthReport { Weak = weak, Reused = reused, Old = old, Breached = breached, Checked = active.Count };
    }
}
