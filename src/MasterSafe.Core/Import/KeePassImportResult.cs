// File:    KeePassImportResult.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core.Import;

/// <summary>Raised when a KeePass export cannot be read or exceeds a safety limit.</summary>
public sealed class KeePassImportException(string message) : Exception(message);

/// <summary>What was found in a KeePass export. Nothing is saved until the user confirms the preview.</summary>
public sealed class KeePassImportResult
{
    /// <summary>Password entries found.</summary>
    public List<PasswordEntry> Passwords { get; } = [];

    /// <summary>Authenticator accounts found in one-time-password fields.</summary>
    public List<OtpAccount> Accounts { get; } = [];

    /// <summary>Entries read from the file (including skipped ones).</summary>
    public int TotalEntries { get; set; }

    /// <summary>Entries in the recycle bin, which are not imported.</summary>
    public int SkippedRecycleBin { get; set; }

    /// <summary>Old versions of entries, which are not imported.</summary>
    public int SkippedHistory { get; set; }

    /// <summary>File attachments, which are not imported.</summary>
    public int SkippedAttachments { get; set; }

    /// <summary>Entries without a title or password and without a usable one-time password.</summary>
    public int SkippedInvalid { get; set; }

    /// <summary>Other things the user should know about.</summary>
    public List<string> Warnings { get; } = [];
}
