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

        var path = Path.Combine(_folder, fileName);
        await File.WriteAllBytesAsync(path, encryptedContent, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        var path = Path.Combine(_folder, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Backup not found.", path);
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
}
