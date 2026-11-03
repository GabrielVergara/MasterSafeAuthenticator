// File:    PasswordEntry.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>What kind of item an entry is. Older versions see every kind as an ordinary entry.</summary>
public enum EntryKind
{
    /// <summary>A sign-in: user name, password and web address.</summary>
    Login = 0,

    /// <summary>A secure note: only the title and the notes matter.</summary>
    Note = 1,

    /// <summary>A payment card, kept in fields.</summary>
    Card = 2,

    /// <summary>Personal details, kept in fields.</summary>
    Identity = 3,
}

/// <summary>A named extra value on an entry. A secret field is hidden on screen and copied like a password.</summary>
public sealed record CustomField
{
    /// <summary>Most fields kept on one entry.</summary>
    public const int MaxPerEntry = 50;

    /// <summary>Name of the field, for example "Card number".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Its value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>True when the value is hidden on screen.</summary>
    public bool IsSecret { get; init; }
}

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

    /// <summary>The secret password. Empty for notes, cards and identities.</summary>
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

    /// <summary>What kind of item this is.</summary>
    public EntryKind Kind { get; init; }

    /// <summary>Extra named values. Cards and identities keep their details here.</summary>
    public IReadOnlyList<CustomField> Fields { get; init; } = [];

    /// <summary>When the password should be changed, if the user set a date.</summary>
    public DateTimeOffset? ExpiresUtc { get; init; }

    /// <summary>A website icon the user accepted: a 32 by 32 PNG, Base64 encoded. Null shows the letter icon.</summary>
    public string? IconPng { get; init; }

    /// <summary>When the entry last went to or from the recycle bin or another folder. Sync uses it to find the newer copy.</summary>
    public DateTimeOffset? StateChangedUtc { get; init; }

    /// <summary>When anything about the entry last changed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset LastChangeUtc => StateChangedUtc is { } state && state > ModifiedUtc ? state : ModifiedUtc;

    /// <summary>Returns a copy of this entry in another folder.</summary>
    public PasswordEntry WithFolder(string folder) => Clone(folder: folder, stateChanged: DateTimeOffset.UtcNow);

    /// <summary>Returns a copy with a folder path cleaned up. Nothing really changed, so sync does not see a change.</summary>
    public PasswordEntry WithCleanedFolder(string folder) => Clone(folder: folder);

    /// <summary>Returns a copy of this entry with a different list of attachments.</summary>
    public PasswordEntry WithAttachments(IReadOnlyList<PasswordAttachment> attachments) => Clone(attachments: attachments);

    /// <summary>Returns a copy of this entry in the recycle bin (<paramref name="when"/> set) or taken out of it (null).</summary>
    public PasswordEntry WithDeleted(DateTimeOffset? when)
        => Clone(deleted: new Optional<DateTimeOffset?>(when), stateChanged: when ?? DateTimeOffset.UtcNow);

    /// <summary>Returns a copy of this entry with a different history.</summary>
    public PasswordEntry WithHistory(IReadOnlyList<PasswordVersion> history) => Clone(history: history);

    /// <summary>Returns a copy with the icon the user accepted, or none.</summary>
    public PasswordEntry WithIcon(string? iconPng) => Clone(icon: new Optional<string?>(iconPng), stateChanged: DateTimeOffset.UtcNow);

    /// <summary>Returns a copy with a web address, for example after moving it to https.</summary>
    public PasswordEntry WithUrl(string url) => Clone(url: url, stateChanged: DateTimeOffset.UtcNow);

    /// <summary>Returns a copy with kind, fields and date restored from what an older version left in the notes.</summary>
    public PasswordEntry WithExtras(string notes, EntryKind kind, IReadOnlyList<CustomField> fields, DateTimeOffset? expires) => new()
    {
        Id = Id,
        Title = Title,
        UserName = UserName,
        Password = Password,
        Url = Url,
        Notes = notes,
        Tags = Tags,
        Folder = Folder,
        Attachments = Attachments,
        History = History,
        DeletedUtc = DeletedUtc,
        Kind = kind,
        Fields = fields,
        ExpiresUtc = expires,
        IconPng = IconPng,
        StateChangedUtc = StateChangedUtc,
        ModifiedUtc = ModifiedUtc,
    };

    /// <summary>The current values as a history entry.</summary>
    public PasswordVersion ToVersion() => new()
    {
        ModifiedUtc = ModifiedUtc,
        Title = Title,
        UserName = UserName,
        Password = Password,
        Url = Url,
        Notes = Notes,
        Fields = Fields,
    };

    private readonly record struct Optional<T>(T Value);

    private PasswordEntry Clone(
        string? folder = null,
        IReadOnlyList<PasswordAttachment>? attachments = null,
        IReadOnlyList<PasswordVersion>? history = null,
        Optional<DateTimeOffset?>? deleted = null,
        Optional<string?>? icon = null,
        string? url = null,
        DateTimeOffset? stateChanged = null) => new()
    {
        Id = Id,
        Title = Title,
        UserName = UserName,
        Password = Password,
        Url = url ?? Url,
        Notes = Notes,
        Tags = Tags,
        Folder = folder ?? Folder,
        Attachments = attachments ?? Attachments,
        History = history ?? History,
        DeletedUtc = deleted is { } d ? d.Value : DeletedUtc,
        Kind = Kind,
        Fields = Fields,
        ExpiresUtc = ExpiresUtc,
        IconPng = icon is { } i ? i.Value : IconPng,
        StateChangedUtc = stateChanged ?? StateChangedUtc,
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

        if (Kind == EntryKind.Login && string.IsNullOrEmpty(Password))
        {
            error = "Password is required.";
            return false;
        }

        error = null;
        return true;
    }
}
