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
    /// <summary>Largest vault file that will be read (100 MiB: 50 MiB of attachments, stored as encoded text, plus the rest).</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>How many daily snapshots of the vault are kept.</summary>
    public const int SnapshotsKept = 7;

    private readonly string _path;
    private readonly string? _snapshotFolder;
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// Creates a repository backed by the supplied file path. When <paramref name="snapshotFolder"/> is given, the first
    /// save of each day first keeps a copy of the vault as it was, so a bad save, sync accident or ransomware does not
    /// take the only copy with it.
    /// </summary>
    public FileVaultRepository(string path, string? snapshotFolder = null, Func<DateTime>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _snapshotFolder = snapshotFolder;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
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
            CreatePrivateDirectory(directory);
        }

        KeepDailySnapshot();

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

    /// <summary>
    /// Copies the vault as it is on disk before the first save of the day, and removes the oldest copies beyond the last
    /// <see cref="SnapshotsKept"/>. Snapshots are the same encrypted file, so they are as safe as the vault itself.
    /// A problem here never stops the save.
    /// </summary>
    private void KeepDailySnapshot()
    {
        if (_snapshotFolder is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            var stem = Path.GetFileNameWithoutExtension(_path);
            var target = Path.Combine(_snapshotFolder, $"{stem}-{_utcNow():yyyyMMdd}.sauth");
            if (File.Exists(target))
            {
                return;
            }

            CreatePrivateDirectory(_snapshotFolder);
            File.Copy(_path, target);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"-\d{8}\.sauth$");
            foreach (var old in Directory.EnumerateFiles(_snapshotFolder)
                .Where(f => pattern.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .Skip(SnapshotsKept))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Snapshots are an extra safety net; the save itself goes on.
        }
    }

    /// <summary>Creates the folder; on Linux only its owner can open it, so other users cannot even list the vault.</summary>
    internal static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(_path));
}
