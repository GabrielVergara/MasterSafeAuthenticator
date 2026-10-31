// File:    PasswordEntry.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

namespace SafeAuthenticator.Core;

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
