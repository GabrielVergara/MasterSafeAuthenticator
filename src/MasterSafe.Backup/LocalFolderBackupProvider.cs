// File:    LocalFolderBackupProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Backup;

/// <summary>
/// Stores encrypted <c>.sauth</c> backups in a local folder. This is the required default provider.
/// </summary>
public sealed class LocalFolderBackupProvider : IBackupProvider
{
    private readonly string _folder;

    public LocalFolderBackupProvider(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    /// <inheritdoc />
    public string Name => "Local Folder";

    /// <inheritdoc />
    public async Task<string> UploadAsync(string fileName, byte[] encryptedContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(encryptedContent);

        var path = SafePath(fileName);

        // Written beside the target and moved in whole, so another computer syncing this folder never reads half a file.
        // A file that is already there is never replaced.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encryptedContent, cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                throw new BackupNameTakenException(fileName);
            }
        }
        finally
        {
            File.Delete(temporary);
        }

        return path;
    }

    /// <summary>
    /// Deletes the oldest of the named files so that <paramref name="keep"/> remain, but only files whose first bytes pass
    /// <paramref name="isOurs"/>, so an ordinary backup in the same folder is never touched. Returns how many went.
    /// </summary>
    public int DeleteOldest(IEnumerable<string> names, Func<byte[], bool> isOurs, int headerLength, int keep)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(isOurs);

        var ours = names.OrderDescending(StringComparer.Ordinal).Where(name =>
        {
            try
            {
                using var file = File.OpenRead(SafePath(name));
                var header = new byte[headerLength];
                return file.ReadAtLeast(header, headerLength, throwOnEndOfStream: false) == headerLength && isOurs(header);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }).ToList();

        var deleted = 0;
        foreach (var name in ours.Skip(keep))
        {
            try
            {
                File.Delete(SafePath(name));
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again next time.
            }
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        var path = SafePath(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Backup not found.", path);
        }

        if (new FileInfo(path).Length > MaxBackupBytes)
        {
            throw new InvalidDataException("The backup is larger than the allowed limit.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(_folder, "*.sauth")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    /// <summary>Largest backup that will be read (100 MiB).</summary>
    public const long MaxBackupBytes = 100L * 1024 * 1024;

    // A backup name must be a plain file name. Names with folders, "..", or a full path are refused so a
    // crafted name can never reach outside the backup folder.
    private string SafePath(string fileName)
    {
        if (fileName != Path.GetFileName(fileName) || fileName is "." or ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.IndexOfAny(['\\', ':']) >= 0)
        {
            throw new ArgumentException("The backup name must be a plain file name.", nameof(fileName));
        }

        return Path.Combine(_folder, fileName);
    }
}
