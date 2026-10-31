using SafeAuthenticator.Core;

namespace SafeAuthenticator.Backup;

/// <summary>
/// Destination for an encrypted vault backup.
/// </summary>
public interface IBackupProvider
{
    /// <summary>Human-readable provider name (e.g. "Local Folder").</summary>
    string Name { get; }

    /// <summary>Writes the encrypted backup bytes under the given file name.</summary>
    Task<string> UploadAsync(string fileName, byte[] encryptedContent, CancellationToken cancellationToken = default);

    /// <summary>Reads the encrypted backup bytes for the given file name.</summary>
    Task<byte[]> DownloadAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Lists available backup file names.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exports and imports encrypted <c>.sauth</c> vault backups. No plaintext export is ever produced.
/// </summary>
public interface IBackupService
{
    /// <summary>Exports accounts to an encrypted <c>.sauth</c> backup via the provider.</summary>
    Task<string> ExportAsync(
        IEnumerable<OtpAccount> accounts,
        string masterPassword,
        IBackupProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>Imports accounts from an encrypted <c>.sauth</c> backup via the provider.</summary>
    Task<IReadOnlyList<OtpAccount>> ImportAsync(
        string fileName,
        string masterPassword,
        IBackupProvider provider,
        CancellationToken cancellationToken = default);
}
