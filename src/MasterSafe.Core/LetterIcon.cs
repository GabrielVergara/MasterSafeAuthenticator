// File:    LetterIcon.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text;

namespace MasterSafe.Core;

/// <summary>
/// The built-in icon of an entry: the first letter of its name on a coloured tile. The colour comes from the name, so an
/// entry keeps its colour. Nothing is downloaded and no logo is copied.
/// </summary>
public static class LetterIcon
{
    /// <summary>How many tile colours there are.</summary>
    public const int ColorCount = 8;

    /// <summary>The letter to show: the first letter or digit of the name, in capitals, or "?".</summary>
    public static string Letter(string? name)
    {
        foreach (var rune in (name ?? string.Empty).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                return Rune.ToUpperInvariant(rune).ToString();
            }
        }

        return "?";
    }

    /// <summary>Which colour a name gets, from 0 to <see cref="ColorCount"/> minus 1. The same name always gets the same one.</summary>
    public static int ColorIndex(string? name)
    {
        // Not string.GetHashCode: that changes every time the program starts.
        uint hash = 2166136261;
        foreach (var c in (name ?? string.Empty).Trim().ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)(hash % ColorCount);
    }
}
