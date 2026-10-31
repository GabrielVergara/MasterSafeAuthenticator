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
        await File.WriteAllBytesAsync(path, encryptedContent, cancellationToken).ConfigureAwait(false);
        return path;
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

    /// <summary>Largest backup that will be read (32 MiB).</summary>
    public const long MaxBackupBytes = 32L * 1024 * 1024;

    // A backup name must be a plain file name. Names with folders, "..", or a full path are refused so a
    // crafted name can never reach outside the backup folder.
    private string SafePath(string fileName)
    {
        if (fileName != Path.GetFileName(fileName) || fileName is "." or ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The backup name must be a plain file name.", nameof(fileName));
        }

        return Path.Combine(_folder, fileName);
    }
}
