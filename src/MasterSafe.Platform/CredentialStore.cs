// File:    CredentialStore.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Platform;

/// <summary>
/// Stores a small secret blob (the vault quick-unlock token) in an OS credential store.
/// </summary>
public interface ICredentialStore
{
    /// <summary>True when the OS credential store is available.</summary>
    bool IsSupported { get; }

    /// <summary>Saves or replaces the secret stored under <paramref name="target"/>.</summary>
    void Save(string target, byte[] secret);

    /// <summary>Reads the secret, or null when absent or unreadable.</summary>
    byte[]? Read(string target);

    /// <summary>Deletes the secret. Returns true when something was removed.</summary>
    bool Delete(string target);
}

/// <summary>
/// In-memory fallback for non-Windows hosts and tests. Nothing is persisted.
/// </summary>
public sealed class HeadlessCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, byte[]> _items = [];

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public void Save(string target, byte[] secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        ArgumentNullException.ThrowIfNull(secret);
        _items[target] = (byte[])secret.Clone();
    }

    /// <inheritdoc />
    public byte[]? Read(string target)
        => _items.TryGetValue(target, out var v) ? (byte[])v.Clone() : null;

    /// <inheritdoc />
    public bool Delete(string target) => _items.Remove(target);
}
