// File:    VaultPathSafety.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>
/// Recognises paths on a network share without touching the network. On Windows, just looking at a file on a share makes the
/// computer try to sign in there, which hands the share's owner a form of the user's network password. The settings file is not
/// protected, so a path in it must be checked before it is used in any way.
/// </summary>
public static class VaultPathSafety
{
    /// <summary>True when <paramref name="path"/> is, or leads through a link to, a network location.</summary>
    public static bool IsNetworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var text = path.Trim().Replace('/', '\\');
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // \\server\share, \\?\UNC\server\share and \??\UNC\server\share. Device paths such as \\?\C:\ are local.
        if (text.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || text.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.StartsWith(@"\\", StringComparison.Ordinal) && !text.StartsWith(@"\\?\", StringComparison.Ordinal) && !text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return true;
        }

        // Only a rooted local path gets this far, so these checks stay on this computer.
        if (text.Length >= 2 && text[1] == ':' && IsMappedDrive(text[..1]))
        {
            return true;
        }

        return LeadsToNetwork(text);
    }

    private static bool IsMappedDrive(string letter)
    {
        try
        {
            return new DriveInfo(letter).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A local folder can be a link to a share, so each step of the path is looked at.
    private static bool LeadsToNetwork(string path)
    {
        try
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                var target = new FileInfo(current).LinkTarget;
                if (target is not null && (target.StartsWith(@"\\", StringComparison.Ordinal) || target.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A path that cannot be looked at is not treated as a share; opening it will fail by itself.
        }

        return false;
    }
}
