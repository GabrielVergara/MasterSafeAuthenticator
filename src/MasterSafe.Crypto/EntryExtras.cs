// File:    EntryExtras.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text.Json;
using MasterSafe.Core;

namespace MasterSafe.Crypto;

/// <summary>
/// Keeps card numbers and other fields safe from an older version. A version that does not know a property leaves it out when
/// it saves, which is harmless for an icon but would lose a card number. So the kind, the fields and the date are also
/// written as one line at the end of the notes, which every version keeps. When this version reads the vault the line is
/// taken out again, and used to restore what an older version dropped. The line is readable in an older version too.
/// </summary>
public static class EntryExtras
{
    /// <summary>The line that starts the block.</summary>
    public const string Marker = "[MasterSafe 1.6: keep this block, it holds this item's extra fields]";

    private sealed record Block
    {
        public EntryKind Kind { get; init; }

        public List<CustomField> Fields { get; init; } = [];

        public DateTimeOffset? Expires { get; init; }
    }

    /// <summary>Returns the entry as it is written to the vault: with the block in its notes, when it needs one.</summary>
    public static PasswordEntry Attach(PasswordEntry entry)
    {
        if (entry.Kind == EntryKind.Login && entry.Fields.Count == 0 && entry.ExpiresUtc is null)
        {
            return entry;
        }

        var block = JsonSerializer.Serialize(new Block { Kind = entry.Kind, Fields = [.. entry.Fields], Expires = entry.ExpiresUtc });
        var notes = entry.Notes.Length == 0 ? string.Empty : entry.Notes + "\n\n";
        return entry.WithExtras($"{notes}{Marker}\n{block}", entry.Kind, entry.Fields, entry.ExpiresUtc);
    }

    /// <summary>Returns the entry as the program uses it: the block is gone from the notes and its values are in place.</summary>
    public static PasswordEntry Detach(PasswordEntry entry)
    {
        var at = entry.Notes.LastIndexOf(Marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return entry;
        }

        Block? block;
        try
        {
            block = JsonSerializer.Deserialize<Block>(entry.Notes[(at + Marker.Length)..].Trim());
        }
        catch (JsonException)
        {
            // Someone edited the line. The text stays in the notes, where it can still be read.
            return entry;
        }

        if (block is null)
        {
            return entry;
        }

        var notes = entry.Notes[..at].TrimEnd('\n', '\r');
        var kind = Enum.IsDefined(block.Kind) ? block.Kind : EntryKind.Login;
        return entry.WithExtras(notes, kind, [.. block.Fields.Take(CustomField.MaxPerEntry)], block.Expires);
    }
}
