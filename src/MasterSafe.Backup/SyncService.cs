// File:    SyncService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.Backup;

/// <summary>
/// The code that lets a second computer join a sync: the 256-bit sync key with a short check value, as groups of letters and
/// digits. It is not derived from any password, so a stolen sync file cannot be guessed at offline.
/// </summary>
public static class SyncCode
{
    private const int KeySize = 32;
    private const int CheckSize = 2;

    /// <summary>Creates a new random key.</summary>
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    /// <summary>The key as a code to write down or paste, for example <c>ABCD-EFGH-...</c>.</summary>
    public static string Format(ReadOnlySpan<byte> key)
    {
        var text = Base32.Encode([.. key, .. SHA256.HashData(key)[..CheckSize]]).TrimEnd('=');
        return string.Join('-', text.Chunk(4).Select(c => new string(c)));
    }

    /// <summary>Reads a code. Case, spaces and dashes do not matter. Null when it is mistyped.</summary>
    public static byte[]? TryParse(string? code)
    {
        var clean = new string((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (!Base32.IsValid(clean))
        {
            return null;
        }

        var bytes = Base32.Decode(clean);
        if (bytes.Length != KeySize + CheckSize)
        {
            return null;
        }

        // The last letter has spare bits, so a different letter there can decode the same. Only the exact code is accepted.
        var key = bytes[..KeySize];
        return SHA256.HashData(key).AsSpan(0, CheckSize).SequenceEqual(bytes.AsSpan(KeySize))
            && Format(key).Replace("-", string.Empty, StringComparison.Ordinal) == clean
            ? key
            : null;
    }
}

/// <summary>
/// One sync file: the data of a vault, compressed and encrypted with the sync key. The file name is bound in, so a server
/// cannot pass one file off as another. Settings, the sync key and the server password are never in it.
/// </summary>
public static class SyncPackage
{
    /// <summary>The first bytes of every sync file.</summary>
    public static ReadOnlySpan<byte> Magic => "MSSYNC1\n"u8;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>True when the bytes start like a sync file. A normal backup can be in the same folder.</summary>
    public static bool IsPackage(ReadOnlySpan<byte> data) => data.StartsWith(Magic);

    /// <summary>Encrypts the data of <paramref name="contents"/> for the file <paramref name="fileName"/>.</summary>
    public static byte[] Seal(VaultContents contents, byte[] key, string fileName, IEncryptionService encryption)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(encryption);

        var plain = VaultContentsCodec.Encode(contents.DataOnly());
        var compressed = BoundedGzip.Compress(plain);
        CryptographicOperations.ZeroMemory(plain);
        var payload = encryption.Encrypt(compressed, key, Aad(fileName));
        CryptographicOperations.ZeroMemory(compressed);

        return [.. Magic, .. payload.Nonce, .. payload.Tag, .. payload.Ciphertext];
    }

    /// <summary>Opens a sync file.</summary>
    /// <exception cref="InvalidPasswordException">The key is wrong, or the file was changed or renamed.</exception>
    /// <exception cref="InvalidDataException">The file is damaged.</exception>
    public static VaultContents Open(byte[] data, byte[] key, string fileName, IEncryptionService encryption)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(encryption);

        var header = Magic.Length + NonceSize + TagSize;
        if (!IsPackage(data) || data.Length < header)
        {
            throw new InvalidDataException("This is not a sync file.");
        }

        var nonce = data[Magic.Length..(Magic.Length + NonceSize)];
        var tag = data[(Magic.Length + NonceSize)..header];
        var decrypted = encryption.Decrypt(new EncryptedPayload(nonce, data[header..], tag), key, Aad(fileName));
        var plain = BoundedGzip.Decompress(decrypted);
        CryptographicOperations.ZeroMemory(decrypted);
        try
        {
            return VaultContentsCodec.Decode(plain).DataOnly();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Aad(string fileName) => Encoding.UTF8.GetBytes($"MasterSafe.Sync|v1|{fileName}");
}

/// <summary>What one sync did.</summary>
/// <param name="Contents">The vault contents after the merge, with the sync settings brought up to date.</param>
/// <param name="Summary">What changed here.</param>
/// <param name="Uploaded">The name of the file written, or null when nothing needed writing.</param>
/// <param name="Rejected">Files that this sync code cannot open.</param>
/// <param name="Accepted">Files that were opened and merged.</param>
/// <param name="FilesInPlace">How many sync files are in the place now.</param>
/// <param name="UploadError">Why the file could not be written, when that failed. What was merged is still good.</param>
public sealed record SyncResult(
    VaultContents Contents, MergeSummary Summary, string? Uploaded, int Rejected, int Accepted, int FilesInPlace, string? UploadError);

/// <summary>
/// Sync through a place the user owns: a folder or a WebDAV server. The place only ever holds encrypted files, and files
/// are only ever added, never changed, because a server may refuse to overwrite or delete (the one this was written for does so
/// by design, so that a stolen server password cannot destroy anything). Each computer reads the files it has not seen, merges
/// them into its vault with <see cref="VaultMerger"/>, and adds one new file when it has something the others lack.
/// </summary>
public sealed partial class SyncService(IEncryptionService encryption, Func<DateTime>? utcNow = null)
{
    /// <summary>Most files read in one run. The rest are read next time.</summary>
    public const int MaxFilesPerRun = 25;

    /// <summary>How many sync files stay in a folder. A server is left alone: this app never deletes anything there.</summary>
    public const int KeepInFolder = 20;

    /// <summary>How many file names are remembered as seen.</summary>
    public const int MaxSeenKept = 2000;

    private const int NameRetries = 5;

    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    // The names the server accepts: vault-YYYYMMDD-HHMMSS.sauth. Sync files share them with ordinary backups.
    [GeneratedRegex(@"\Avault-[0-9]{8}-[0-9]{6}\.sauth\z", RegexOptions.CultureInvariant)]
    private static partial Regex SyncName();

    /// <summary>Reads what is new, merges it into <paramref name="local"/> and writes a new file when the others need one.</summary>
    /// <exception cref="BackupProviderException">The place could not be reached.</exception>
    public async Task<SyncResult> RunAsync(VaultContents local, IBackupProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(provider);
        var settings = local.Sync ?? throw new InvalidOperationException("Sync is not set up.");
        var key = Convert.FromBase64String(settings.Key);

        try
        {
            var names = (await provider.ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(n => SyncName().IsMatch(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var seen = new HashSet<string>(settings.SeenFiles, StringComparer.Ordinal);
            var merged = local;
            var summary = new MergeSummary(0, 0, 0, 0);
            var remoteStates = new HashSet<string>(StringComparer.Ordinal);
            var rejected = 0;
            var accepted = 0;

            foreach (var name in names.Where(n => !seen.Contains(n)).OrderDescending(StringComparer.Ordinal).Take(MaxFilesPerRun).ToList())
            {
                var bytes = await provider.DownloadAsync(name, cancellationToken).ConfigureAwait(false);
                if (SyncPackage.IsPackage(bytes))
                {
                    try
                    {
                        var remote = SyncPackage.Open(bytes, key, name, encryption);
                        (merged, var step) = VaultMerger.Merge(merged, remote);
                        summary = new MergeSummary(
                            summary.Added + step.Added, summary.Updated + step.Updated, summary.Conflicts + step.Conflicts, summary.Removed + step.Removed);
                        remoteStates.Add(VaultMerger.StateHash(remote));
                        accepted++;
                    }
                    catch (Exception ex) when (ex is InvalidPasswordException or InvalidDataException)
                    {
                        // Made with another sync code, or changed on the way. Nothing from it is used.
                        rejected++;
                    }
                }

                seen.Add(name);
            }

            var state = VaultMerger.StateHash(merged);
            var hasData = merged.Accounts.Count > 0 || merged.Passwords.Count > 0;
            string? uploaded = null;
            string? uploadError = null;

            // Nothing is written when the place already holds this state, so two computers do not answer each other for ever.
            if (hasData && state != settings.LastStateHash && !remoteStates.Contains(state) && !(rejected > 0 && accepted == 0))
            {
                try
                {
                    uploaded = await UploadAsync(merged, key, provider, seen, cancellationToken).ConfigureAwait(false);
                }
                catch (BackupProviderException ex)
                {
                    uploadError = ex.Message;
                }
            }

            var filesInPlace = names.Count + (uploaded is null ? 0 : 1);
            if (provider is LocalFolderBackupProvider folder)
            {
                var present = uploaded is null ? names : [.. names, uploaded];
                filesInPlace -= folder.DeleteOldest(present, static header => SyncPackage.IsPackage(header), SyncPackage.Magic.Length, KeepInFolder);
            }

            var updated = settings with
            {
                SeenFiles = seen.OrderDescending(StringComparer.Ordinal).Take(MaxSeenKept).ToList(),
                LastStateHash = uploadError is null ? state : settings.LastStateHash,
                LastSyncUtc = new DateTimeOffset(_utcNow(), TimeSpan.Zero),
            };

            return new SyncResult(merged with { Sync = updated }, summary, uploaded, rejected, accepted, filesInPlace, uploadError);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<string> UploadAsync(VaultContents contents, byte[] key, IBackupProvider provider, HashSet<string> seen, CancellationToken cancellationToken)
    {
        // A name that is taken is never overwritten: another computer got there in the same second, so the next one is used.
        var time = _utcNow();
        for (var attempt = 0; ; attempt++)
        {
            var name = $"vault-{time.AddSeconds(attempt):yyyyMMdd-HHmmss}.sauth";
            if (seen.Contains(name))
            {
                continue;
            }

            var bytes = SyncPackage.Seal(contents, key, name, encryption);
            if (bytes.Length > WebDavBackupProvider.MaxDownloadBytes)
            {
                throw new BackupProviderException("The vault is too large to sync (more than 100 MiB).");
            }

            try
            {
                await provider.UploadAsync(name, bytes, cancellationToken).ConfigureAwait(false);
                seen.Add(name);
                return name;
            }
            catch (BackupNameTakenException) when (attempt < NameRetries)
            {
                // Try the next second.
            }
        }
    }
}
