// File:    PasswordVersion.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>An earlier state of a password entry, kept in its history.</summary>
public sealed class PasswordVersion
{
    /// <summary>Most versions kept for one entry.</summary>
    public const int MaxKept = 10;

    /// <summary>How long entries stay in the recycle bin before they are removed for good.</summary>
    public const int BinRetentionDays = 30;

    /// <summary>When this version was saved.</summary>
    public DateTimeOffset ModifiedUtc { get; init; }

    /// <summary>Title at that time.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>User name at that time.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Password at that time.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Web address at that time.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Notes at that time.</summary>
    public string Notes { get; init; } = string.Empty;
}
