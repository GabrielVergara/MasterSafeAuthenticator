// File:    VaultMerger.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text.Json;
using MasterSafe.Core;

namespace MasterSafe.Crypto;

/// <summary>What a merge did.</summary>
/// <param name="Added">Items that were only in the other copy.</param>
/// <param name="Updated">Items that were newer in the other copy.</param>
/// <param name="Conflicts">Items changed on both sides. The older values were kept in the entry's history.</param>
/// <param name="Removed">Items deleted for good on the other side.</param>
public sealed record MergeSummary(int Added, int Updated, int Conflicts, int Removed)
{
    /// <summary>True when the merge changed nothing here.</summary>
    public bool NothingChanged => Added == 0 && Updated == 0 && Conflicts == 0 && Removed == 0;
}

/// <summary>
/// Joins two copies of a vault's data. The result is the same whichever copy comes first, and merging the same copy twice
/// changes nothing, so computers can sync in any order and end up equal.
/// <list type="bullet">
/// <item>An item that is only in one copy is kept.</item>
/// <item>When both copies changed an item, the newer one wins and the older values go into the entry's history, so no
/// password is lost to a merge.</item>
/// <item>An item is only removed when it was deleted for good, and only when it was not changed after that.</item>
/// </list>
/// </summary>
public static class VaultMerger
{
    /// <summary>Joins <paramref name="remote"/> into <paramref name="local"/>. The settings of <paramref name="local"/> are kept.</summary>
    public static (VaultContents Merged, MergeSummary Summary) Merge(VaultContents local, VaultContents remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var tombstones = MergeTombstones(local.Tombstones, remote.Tombstones);
        var added = 0;
        var updated = 0;
        var conflicts = 0;
        var removed = 0;

        var remoteEntries = First(remote.Passwords, e => e.Id);
        var passwords = new List<PasswordEntry>();
        foreach (var mine in local.Passwords.DistinctBy(e => e.Id))
        {
            remoteEntries.Remove(mine.Id, out var theirs);
            var (merged, outcome) = theirs is null ? (mine, Outcome.Same) : MergeEntry(mine, theirs);
            if (tombstones.TryGetValue(merged.Id, out var gone) && gone >= merged.LastChangeUtc)
            {
                removed++;
                continue;
            }

            passwords.Add(merged);
            updated += outcome == Outcome.TheirsNewer ? 1 : 0;
            conflicts += outcome == Outcome.Conflict ? 1 : 0;
        }

        foreach (var theirs in remoteEntries.Values)
        {
            if (!tombstones.TryGetValue(theirs.Id, out var gone) || gone < theirs.LastChangeUtc)
            {
                passwords.Add(theirs);
                added++;
            }
        }

        var remoteAccounts = First(remote.Accounts, a => a.Id);
        var accounts = new List<OtpAccount>();
        foreach (var mine in local.Accounts.DistinctBy(a => a.Id))
        {
            remoteAccounts.Remove(mine.Id, out var theirs);
            var merged = theirs is null ? mine : MergeAccount(mine, theirs);
            if (tombstones.TryGetValue(merged.Id, out var gone) && gone >= (merged.ModifiedUtc ?? DateTimeOffset.MinValue))
            {
                removed++;
                continue;
            }

            accounts.Add(merged);
            updated += theirs is not null && !SameAccount(merged, mine) ? 1 : 0;
        }

        foreach (var theirs in remoteAccounts.Values)
        {
            if (!tombstones.TryGetValue(theirs.Id, out var gone) || gone < (theirs.ModifiedUtc ?? DateTimeOffset.MinValue))
            {
                accounts.Add(theirs);
                added++;
            }
        }

        var attachments = new Dictionary<Guid, byte[]>();
        foreach (var attachment in passwords.SelectMany(p => p.Attachments))
        {
            if (local.AttachmentData.TryGetValue(attachment.Id, out var bytes) || remote.AttachmentData.TryGetValue(attachment.Id, out bytes))
            {
                attachments[attachment.Id] = bytes;
            }
        }

        var folderIcons = new Dictionary<string, string>(remote.FolderIcons);
        foreach (var (path, icon) in local.FolderIcons)
        {
            folderIcons[path] = icon;
        }

        var merged2 = local with
        {
            Accounts = accounts,
            Passwords = KeepAttachmentsWithin(passwords, attachments),
            Folders = local.Folders.Union(remote.Folders, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            FolderIcons = folderIcons,
            Tombstones = tombstones.OrderByDescending(t => t.Value).ThenBy(t => t.Key).Take(Tombstone.MaxKept)
                .Select(t => new Tombstone { Id = t.Key, DeletedUtc = t.Value }).ToList(),
            AttachmentData = attachments,
        };

        return (merged2, new MergeSummary(added, updated, conflicts, removed));
    }

    /// <summary>
    /// A fingerprint of the data that does not depend on the order things are stored in, so two computers holding the same
    /// data get the same value.
    /// </summary>
    public static string StateHash(VaultContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var canonical = new
        {
            Accounts = contents.Accounts.OrderBy(a => a.Id).ToList(),
            Passwords = contents.Passwords.OrderBy(p => p.Id).ToList(),
            Folders = contents.Folders.Order(StringComparer.Ordinal).ToList(),
            FolderIcons = contents.FolderIcons.OrderBy(i => i.Key, StringComparer.Ordinal).ToList(),
            Tombstones = contents.Tombstones.OrderBy(t => t.Id).ToList(),
        };

        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }

    private enum Outcome
    {
        Same,
        MineNewer,
        TheirsNewer,
        Conflict,
    }

    private static Dictionary<Guid, T> First<T>(IEnumerable<T> items, Func<T, Guid> id)
        => items.DistinctBy(id).ToDictionary(id);

    private static Dictionary<Guid, DateTimeOffset> MergeTombstones(IEnumerable<Tombstone> a, IEnumerable<Tombstone> b)
    {
        var result = new Dictionary<Guid, DateTimeOffset>();
        foreach (var tombstone in a.Concat(b))
        {
            if (!result.TryGetValue(tombstone.Id, out var known) || tombstone.DeletedUtc > known)
            {
                result[tombstone.Id] = tombstone.DeletedUtc;
            }
        }

        return result;
    }

    private static (PasswordEntry Merged, Outcome Outcome) MergeEntry(PasswordEntry mine, PasswordEntry theirs)
    {
        var sameContent = SameEntry(mine, theirs);
        var mineWins = Prefer(mine, theirs);
        var winner = mineWins ? mine : theirs;
        var loser = mineWins ? theirs : mine;

        // Both sides may hold versions the other has not seen.
        var history = new List<PasswordVersion>(winner.History.Concat(loser.History));
        var conflict = !sameContent && ValuesDiffer(winner, loser);
        if (conflict)
        {
            history.Add(loser.ToVersion());
        }

        var kept = history
            .DistinctBy(v => (v.ModifiedUtc, v.Title, v.UserName, v.Password, v.Url, v.Notes))
            .OrderByDescending(v => v.ModifiedUtc)
            .ThenBy(v => v.Password, StringComparer.Ordinal)
            .Take(PasswordVersion.MaxKept)
            .ToList();

        var attachments = winner.Attachments.Concat(loser.Attachments).DistinctBy(a => a.Id).OrderBy(a => a.Id).ToList();
        var merged = winner.WithHistory(kept).WithAttachments(attachments);
        if (sameContent)
        {
            return (merged, Outcome.Same);
        }

        return (merged, conflict ? Outcome.Conflict : mineWins ? Outcome.MineNewer : Outcome.TheirsNewer);
    }

    // The newer copy wins. With equal times the choice only has to be the same on every computer.
    private static bool Prefer(PasswordEntry a, PasswordEntry b)
    {
        var order = a.LastChangeUtc.CompareTo(b.LastChangeUtc);
        return order != 0
            ? order > 0
            : string.CompareOrdinal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b)) >= 0;
    }

    private static bool ValuesDiffer(PasswordEntry a, PasswordEntry b)
        => a.Title != b.Title || a.UserName != b.UserName || a.Password != b.Password || a.Url != b.Url || a.Notes != b.Notes
           || !a.Fields.SequenceEqual(b.Fields);

    private static bool SameEntry(PasswordEntry a, PasswordEntry b)
        => !ValuesDiffer(a, b) && a.Folder == b.Folder && a.DeletedUtc == b.DeletedUtc && a.Kind == b.Kind
           && a.ExpiresUtc == b.ExpiresUtc && a.IconPng == b.IconPng && a.Tags.SequenceEqual(b.Tags);

    private static OtpAccount MergeAccount(OtpAccount mine, OtpAccount theirs)
    {
        var mineTime = mine.ModifiedUtc ?? DateTimeOffset.MinValue;
        var theirsTime = theirs.ModifiedUtc ?? DateTimeOffset.MinValue;
        var mineWins = mineTime != theirsTime
            ? mineTime > theirsTime
            : string.CompareOrdinal(JsonSerializer.Serialize(mine), JsonSerializer.Serialize(theirs)) >= 0;
        var winner = mineWins ? mine : theirs;

        // A counter must never go back, or an old code would be shown again.
        return winner.WithCounter(Math.Max(mine.Counter, theirs.Counter));
    }

    private static bool SameAccount(OtpAccount a, OtpAccount b)
        => a.Issuer == b.Issuer && a.Label == b.Label && a.Group == b.Group && a.Secret == b.Secret && a.Counter == b.Counter;

    // The two copies can together hold more attached files than a vault allows. Then the files of the newest entries stay.
    private static List<PasswordEntry> KeepAttachmentsWithin(List<PasswordEntry> passwords, Dictionary<Guid, byte[]> data)
    {
        long total = 0;
        var count = 0;
        var allowed = new HashSet<Guid>();
        foreach (var attachment in passwords.OrderByDescending(p => p.LastChangeUtc).ThenBy(p => p.Id).SelectMany(p => p.Attachments))
        {
            if (data.TryGetValue(attachment.Id, out var bytes) && count < AttachmentLimits.MaxCount && total + bytes.Length <= AttachmentLimits.MaxTotalBytes)
            {
                allowed.Add(attachment.Id);
                total += bytes.Length;
                count++;
            }
        }

        foreach (var id in data.Keys.Where(k => !allowed.Contains(k)).ToList())
        {
            data.Remove(id);
        }

        return passwords
            .Select(p => p.Attachments.All(a => allowed.Contains(a.Id)) ? p : p.WithAttachments(p.Attachments.Where(a => allowed.Contains(a.Id)).ToList()))
            .ToList();
    }
}
