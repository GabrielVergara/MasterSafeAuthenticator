// File:    PasswordEntry.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>
/// A KeePass-style stored credential: title, user name, password, URL, notes and tags.
/// Persisted only inside the encrypted vault.
/// </summary>
public sealed class PasswordEntry
{
    /// <summary>Stable identifier for persistence.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display title (e.g. "GitHub").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>User name or email.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>The secret password.</summary>
    public required string Password { get; init; }

    /// <summary>Optional site URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Optional free-form notes.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Optional tags for grouping.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Folder the entry is in, for example <c>Email/Work</c>. Empty means the top level.</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>Files attached to the entry. The bytes are kept separately in the vault.</summary>
    public IReadOnlyList<PasswordAttachment> Attachments { get; init; } = [];

    /// <summary>Older versions of the entry, newest first. Kept so that an overwritten password can be found again.</summary>
    public IReadOnlyList<PasswordVersion> History { get; init; } = [];

    /// <summary>When the entry was moved to the recycle bin; null for an entry that is in use.</summary>
    public DateTimeOffset? DeletedUtc { get; init; }

    /// <summary>True while the entry is in the recycle bin.</summary>
    public bool IsDeleted => DeletedUtc is not null;

    /// <summary>Returns a copy of this entry in another folder.</summary>
    public PasswordEntry WithFolder(string folder) => Clone(folder: folder);

    /// <summary>Returns a copy of this entry with a different list of attachments.</summary>
    public PasswordEntry WithAttachments(IReadOnlyList<PasswordAttachment> attachments) => Clone(attachments: attachments);

    /// <summary>Returns a copy of this entry in the recycle bin (<paramref name="when"/> set) or taken out of it (null).</summary>
    public PasswordEntry WithDeleted(DateTimeOffset? when) => Clone(deleted: new Optional<DateTimeOffset?>(when));

    /// <summary>Returns a copy of this entry with a different history.</summary>
    public PasswordEntry WithHistory(IReadOnlyList<PasswordVersion> history) => Clone(history: history);

    /// <summary>The current values as a history entry.</summary>
    public PasswordVersion ToVersion() => new()
    {
        ModifiedUtc = ModifiedUtc,
        Title = Title,
        UserName = UserName,
        Password = Password,
        Url = Url,
        Notes = Notes,
    };

    private readonly record struct Optional<T>(T Value);

    private PasswordEntry Clone(
        string? folder = null,
        IReadOnlyList<PasswordAttachment>? attachments = null,
        IReadOnlyList<PasswordVersion>? history = null,
        Optional<DateTimeOffset?>? deleted = null) => new()
    {
        Id = Id,
        Title = Title,
        UserName = UserName,
        Password = Password,
        Url = Url,
        Notes = Notes,
        Tags = Tags,
        Folder = folder ?? Folder,
        Attachments = attachments ?? Attachments,
        History = history ?? History,
        DeletedUtc = deleted is { } d ? d.Value : DeletedUtc,
        ModifiedUtc = ModifiedUtc,
    };

    /// <summary>UTC time the entry was last modified.</summary>
    public DateTimeOffset ModifiedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Validates required fields.</summary>
    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            error = "Title is required.";
            return false;
        }

        if (string.IsNullOrEmpty(Password))
        {
            error = "Password is required.";
            return false;
        }

        error = null;
        return true;
    }
}
