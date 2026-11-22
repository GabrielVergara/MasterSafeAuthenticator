// File:    InstalledFiles.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;

namespace MasterSafe.PostQuantum;

/// <summary>What <see cref="InstalledFiles.Check"/> found in the program folder.</summary>
/// <param name="Available">False when the folder has no list (a build from source, or a package that does not ship it).</param>
/// <param name="Version">The version the list belongs to.</param>
/// <param name="Platform">The platform the list belongs to, for example win-x64.</param>
/// <param name="Changed">Listed files whose contents differ.</param>
/// <param name="Missing">Listed files that are not there.</param>
/// <param name="Unexpected">Program files (.dll, .exe, .so) that are not in the list, for example a planted library.</param>
public sealed record InstalledFilesReport(
    bool Available, string? Version, string? Platform,
    IReadOnlyList<string> Changed, IReadOnlyList<string> Missing, IReadOnlyList<string> Unexpected)
{
    /// <summary>True when every listed file is there unchanged and nothing that runs code was added.</summary>
    public bool Ok => Available && Changed.Count == 0 && Missing.Count == 0 && Unexpected.Count == 0;

    /// <summary>The report for a folder without a list.</summary>
    public static InstalledFilesReport NotAvailable { get; } = new(false, null, null, [], [], []);
}

/// <summary>
/// The list of the files a release installs, with the SHA-256 of each, in the format of <c>sha256sum</c> after one header
/// line. The build writes it into the program folder; the same list is a file of the release, so the signed release
/// manifest vouches for it. At start-up the app compares its own files with the list, which notices a replaced or planted
/// file. Someone who can change the program folder can also change the list there, which is why the app also compares it
/// with the signed copy of the release when it is allowed to go online.
/// </summary>
public static class InstalledFiles
{
    /// <summary>The name of the list in the program folder.</summary>
    public const string FileName = "installed-files.txt";

    /// <summary>A real list is a few kilobytes; anything much larger is not one.</summary>
    public const int MaxBytes = 1024 * 1024;

    private const string Header = "# MasterSafe Authenticator installed files ";

    // Files the installer adds next to the program (Inno Setup's uninstaller), which cannot be in the list.
    private static readonly string[] AddedByInstaller = ["unins000.exe", "unins000.dat", "unins000.msg", "LICENSE"];

    private static readonly string[] CodeExtensions = [".dll", ".exe", ".so", ".dylib"];

    /// <summary>The name of the list as a file of the release.</summary>
    public static string AssetName(string version, string platform) => $"MasterSafe-Authenticator-{version}-{platform}-installed-files.txt";

    /// <summary>Writes the list for every file in <paramref name="folder"/>, sorted, with forward slashes.</summary>
    public static byte[] Build(string folder, string version, string platform)
    {
        var text = new StringBuilder();
        text.Append(Header).Append(version).Append(' ').Append(platform).Append('\n');
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                     .Select(p => Relative(folder, p))
                     .Where(p => p != FileName)
                     .Order(StringComparer.Ordinal))
        {
            text.Append(Hash(Path.Combine(folder, path))).Append("  ").Append(path).Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>Reads a list; null when it is not one.</summary>
    public static (string Version, string Platform, IReadOnlyDictionary<string, string> Files)? Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes)
        {
            return null;
        }

        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith(Header, StringComparison.Ordinal))
        {
            return null;
        }

        var head = lines[0][Header.Length..].Split(' ');
        if (head.Length != 2)
        {
            return null;
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            // "<64 hex>  <path>": a path that climbs out of the folder or is rooted is not accepted.
            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ' || !line[..64].All(Uri.IsHexDigit))
            {
                return null;
            }

            var path = line[66..];
            if (path.Contains("..", StringComparison.Ordinal) || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal) || path.Contains('\\'))
            {
                return null;
            }

            files[path] = line[..64].ToLowerInvariant();
        }

        return (head[0], head[1], files);
    }

    /// <summary>Compares the program folder with its list.</summary>
    public static InstalledFilesReport Check(string folder)
    {
        var listPath = Path.Combine(folder, FileName);
        if (!File.Exists(listPath) || new FileInfo(listPath).Length > MaxBytes || Parse(File.ReadAllBytes(listPath)) is not { } list)
        {
            return InstalledFilesReport.NotAvailable;
        }

        var changed = new List<string>();
        var missing = new List<string>();
        foreach (var (path, hash) in list.Files)
        {
            var full = Path.Combine(folder, path);
            if (!File.Exists(full))
            {
                missing.Add(path);
            }
            else if (!string.Equals(Hash(full), hash, StringComparison.Ordinal))
            {
                changed.Add(path);
            }
        }

        var unexpected = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(p => Relative(folder, p))
            .Where(p => !list.Files.ContainsKey(p) && !AddedByInstaller.Contains(p, StringComparer.OrdinalIgnoreCase)
                        && CodeExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new InstalledFilesReport(true, list.Version, list.Platform, changed, missing, unexpected);
    }

    private static string Relative(string folder, string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
