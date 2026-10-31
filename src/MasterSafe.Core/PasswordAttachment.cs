// File:    PasswordAttachment.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>
/// A file attached to a password entry. This is only the description; the bytes are stored next to it in the encrypted
/// vault and are found by <see cref="Id"/>.
/// </summary>
public sealed class PasswordAttachment
{
    /// <summary>Identifies the file's bytes in the vault.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>File name, without a folder.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    public long Size { get; init; }
}

/// <summary>Limits for attached files. They keep the vault small enough to save quickly and guard against hostile files.</summary>
public static class AttachmentLimits
{
    /// <summary>Largest single file (5 MiB).</summary>
    public const long MaxFileBytes = 5L * 1024 * 1024;

    /// <summary>Largest total of all attachments in one vault (50 MiB).</summary>
    public const long MaxTotalBytes = 50L * 1024 * 1024;

    /// <summary>Most attachments in one vault.</summary>
    public const int MaxCount = 1000;

    /// <summary>Longest file name that is kept.</summary>
    public const int MaxNameLength = 150;

    /// <summary>
    /// Turns a name from a file, a vault or an import into a plain file name: no folders, no characters that are not
    /// allowed in file names, not too long, never empty.
    /// </summary>
    public static string SafeName(string? name)
    {
        var plain = (name ?? string.Empty).Replace('\\', '/');
        plain = plain[(plain.LastIndexOf('/') + 1)..];

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(plain.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');

        if (cleaned.Length > MaxNameLength)
        {
            var ext = Path.GetExtension(cleaned);
            ext = ext.Length is > 0 and <= 20 ? ext : string.Empty;
            cleaned = cleaned[..(MaxNameLength - ext.Length)] + ext;
        }

        return cleaned.Length == 0 || cleaned is "." or ".." ? "attachment" : cleaned;
    }

    /// <summary>Shows a size for people, for example "3.2 MB".</summary>
    public static string Describe(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}
