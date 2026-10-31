// File:    FileVaultRepository.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text.Json;

namespace MasterSafe.Crypto;

/// <summary>
/// Stores the encrypted vault as a JSON file on disk. Saves are atomic: the new file is written
/// and flushed next to the old one and then swapped in, and the previous version is kept as
/// <c>.bak</c>, so a crash or power loss cannot leave a half-written vault.
/// </summary>
public sealed class FileVaultRepository : IVaultRepository
{
    /// <summary>Largest vault file that will be read (16 MiB).</summary>
    public const long MaxFileBytes = 16L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    /// <summary>Creates a repository backed by the supplied file path.</summary>
    public FileVaultRepository(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    /// <summary>Path of the previous version kept after each save.</summary>
    public string BackupPath => _path + ".bak";

    /// <inheritdoc />
    public async Task<VaultFile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        if (new FileInfo(_path).Length > MaxFileBytes)
        {
            throw new InvalidDataException("The vault file is larger than the allowed limit.");
        }

        await using var stream = File.OpenRead(_path);
        try
        {
            return await JsonSerializer.DeserializeAsync<VaultFile>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The vault file is damaged or is not a MasterSafe vault.", ex);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(VaultFile vault, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vault);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        await using (var stream = new FileStream(
            temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, vault, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        if (File.Exists(_path))
        {
            File.Replace(temp, _path, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, _path);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(_path));
}
