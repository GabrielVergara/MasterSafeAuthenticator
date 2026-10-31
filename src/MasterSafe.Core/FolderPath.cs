// File:    FolderPath.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>
/// Folder paths for password entries, written like <c>Email/Work</c>. The empty string is the top level.
/// Paths from files and from the user are cleaned up so they always have the same shape.
/// </summary>
public static class FolderPath
{
    /// <summary>Separates the folders in a path.</summary>
    public const char Separator = '/';

    /// <summary>Deepest nesting that is kept.</summary>
    public const int MaxDepth = 8;

    /// <summary>Longest folder name that is kept.</summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Cleans a path: both slash styles separate folders, names are trimmed, empty names are dropped, and names and
    /// depth are limited. Never throws; unusable input gives the top level.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var parts = path.Replace('\\', Separator)
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Length > MaxNameLength ? p[..MaxNameLength].TrimEnd() : p)
            .Where(p => p.Length > 0 && p is not "." and not "..")
            .Take(MaxDepth);

        return string.Join(Separator, parts);
    }

    /// <summary>The last folder name of a path, or an empty string for the top level.</summary>
    public static string Name(string path)
    {
        var slash = path.LastIndexOf(Separator);
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>The path above <paramref name="path"/>, or an empty string when it is already at the top.</summary>
    public static string Parent(string path)
    {
        var slash = path.LastIndexOf(Separator);
        return slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>Puts <paramref name="name"/> under <paramref name="parent"/> and cleans the result.</summary>
    public static string Combine(string parent, string name)
        => Normalize(string.IsNullOrEmpty(parent) ? name : parent + Separator + name);

    /// <summary>The number of folders in a path (0 for the top level).</summary>
    public static int Depth(string path) => path.Length == 0 ? 0 : path.Count(c => c == Separator) + 1;

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or one of the folders below it.</summary>
    public static bool IsInside(string path, string folder)
        => folder.Length == 0
           || path.Equals(folder, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(folder + Separator, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder itself and every folder above it, from the top down.</summary>
    public static IEnumerable<string> WithAncestors(string path)
    {
        var current = string.Empty;
        foreach (var part in path.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? part : current + Separator + part;
            yield return current;
        }
    }
}
