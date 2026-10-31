// File:    SwitchableVaultRepository.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Crypto;

/// <summary>
/// A vault repository whose file can be changed while the application runs, so the user can open another vault
/// file or start a new vault without restarting. All reads and writes go to the current path.
/// </summary>
public sealed class SwitchableVaultRepository : IVaultRepository
{
    private volatile FileVaultRepository _inner;

    /// <summary>Creates the repository pointing at <paramref name="path"/>.</summary>
    public SwitchableVaultRepository(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _inner = new FileVaultRepository(path);
        Path = path;
    }

    /// <summary>The vault file currently in use.</summary>
    public string Path { get; private set; }

    /// <summary>Points the repository at another vault file. The file does not need to exist yet.</summary>
    public void SetPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _inner = new FileVaultRepository(path);
        Path = path;
    }

    /// <inheritdoc />
    public Task<VaultFile?> LoadAsync(CancellationToken cancellationToken = default) => _inner.LoadAsync(cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(VaultFile vault, CancellationToken cancellationToken = default) => _inner.SaveAsync(vault, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => _inner.ExistsAsync(cancellationToken);
}
