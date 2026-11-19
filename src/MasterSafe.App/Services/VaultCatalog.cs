// File:    VaultCatalog.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Crypto;

namespace MasterSafe.App.Services;

/// <summary>Where a vault file lives.</summary>
public enum VaultKind
{
    /// <summary>The standard vault in the app's data folder.</summary>
    Default,

    /// <summary>An old vault moved aside into the archive folder.</summary>
    Archived,

    /// <summary>A vault file opened from somewhere else. The app never moves or deletes these.</summary>
    External,
}

/// <summary>The path is on a network share and the user has not agreed to that yet.</summary>
public sealed class NetworkVaultException(string path) : Exception("That vault file is on a network share.")
{
    /// <summary>The path that was refused.</summary>
    public string Path { get; } = path;
}

/// <summary>A vault file the app knows about.</summary>
public sealed record VaultEntry(string Path, string Name, VaultKind Kind, DateTimeOffset Modified, long Size, bool IsActive);

/// <summary>A daily copy of the main vault. <paramref name="Saved"/> is when the vault in it was last saved.</summary>
public sealed record SnapshotEntry(string Path, DateTimeOffset Saved, long Size);

/// <summary>
/// Knows which vault files exist and which one is in use. It can move an old vault into the archive (for example
/// after the master password was lost), switch to another vault file, and delete archived vaults. Nothing is
/// ever overwritten: before a new vault is created, any vault already at the default path is archived first.
/// </summary>
public sealed class VaultCatalog
{
    private readonly AppPaths _paths;
    private readonly SwitchableVaultRepository _repository;
    private readonly ISettingsStore _settings;

    /// <summary>Creates the catalog and points the repository at the vault saved in the settings.</summary>
    public VaultCatalog(AppPaths paths, SwitchableVaultRepository repository, ISettingsStore settings)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // The settings file is not protected. The path in it is checked before anything touches it, because on Windows even
        // asking whether a file exists on a share sends a form of the user's network password there.
        var saved = settings.Load().ActiveVaultPath;
        if (VaultPathSafety.IsNetworkPath(saved))
        {
            RefusedNetworkPath = saved;
            saved = null;
        }

        _repository.SetPath(!string.IsNullOrWhiteSpace(saved) && File.Exists(saved) ? saved : _paths.DefaultVaultPath);
    }

    private readonly HashSet<string> _allowedNetworkPaths = new(PathComparer);

    /// <summary>A network path from the settings file that was not opened. The user decides on the sign-in screen.</summary>
    public string? RefusedNetworkPath { get; private set; }

    /// <summary>The user agreed to open this path from a network share, for this run only. A saved "yes" could be forged.</summary>
    public void AllowNetworkPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _allowedNetworkPaths.Add(path);
        RefusedNetworkPath = null;
    }

    /// <summary>The user chose not to open the network path.</summary>
    public void DismissNetworkPath() => RefusedNetworkPath = null;

    private bool MayTouch(string path) => !VaultPathSafety.IsNetworkPath(path) || _allowedNetworkPaths.Contains(path);

    /// <summary>The vault file in use.</summary>
    public string ActivePath => _repository.Path;

    /// <summary>All vault files the app knows about, the active one first.</summary>
    public IReadOnlyList<VaultEntry> List()
    {
        var entries = new Dictionary<string, VaultEntry>(PathComparer);

        void Add(string path, VaultKind kind)
        {
            if (!MayTouch(path))
            {
                return;
            }

            var info = new FileInfo(path);
            if (info.Exists && !entries.ContainsKey(info.FullName))
            {
                entries[info.FullName] = new VaultEntry(
                    info.FullName, info.Name, kind, info.LastWriteTimeUtc, info.Length, Same(info.FullName, ActivePath));
            }
        }

        Add(_paths.DefaultVaultPath, VaultKind.Default);
        if (Directory.Exists(_paths.ArchiveFolder))
        {
            foreach (var file in Directory.EnumerateFiles(_paths.ArchiveFolder, "*.sauth").Order(StringComparer.OrdinalIgnoreCase))
            {
                Add(file, VaultKind.Archived);
            }
        }

        foreach (var known in _settings.Load().KnownVaultPaths)
        {
            Add(known, _paths.IsInsideDataFolder(known) ? VaultKind.Archived : VaultKind.External);
        }

        Add(ActivePath, _paths.IsInsideDataFolder(ActivePath) ? VaultKind.Default : VaultKind.External);

        return entries.Values
            .OrderByDescending(e => e.IsActive)
            .ThenBy(e => e.Kind)
            .ThenByDescending(e => e.Modified)
            .ToList();
    }

    /// <summary>
    /// Moves the vault at the default path (and its <c>.bak</c>) into the archive folder and returns the new path,
    /// or null when there is nothing to archive. Vault files outside the app's folder are never moved.
    /// </summary>
    public string? ArchiveDefaultVault()
    {
        var source = _paths.DefaultVaultPath;
        if (!File.Exists(source))
        {
            return null;
        }

        CreatePrivateDirectory(_paths.ArchiveFolder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var target = Path.Combine(_paths.ArchiveFolder, $"vault-{stamp}.sauth");
        for (var n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(_paths.ArchiveFolder, $"vault-{stamp}-{n}.sauth");
        }

        File.Move(source, target);
        if (File.Exists(source + ".bak"))
        {
            File.Move(source + ".bak", target + ".bak");
        }

        // The archived vault is the copy that is kept. Its snapshots would otherwise outlive a later "delete forever".
        var snapshots = Path.Combine(_paths.DataFolder, "snapshots");
        if (Directory.Exists(snapshots))
        {
            foreach (var snapshot in Directory.EnumerateFiles(snapshots).Where(f => FileVaultRepository.IsSnapshotOf(source, f)).ToList())
            {
                File.Delete(snapshot);
            }
        }

        if (Same(ActivePath, source))
        {
            Activate(_paths.DefaultVaultPath);
        }

        return target;
    }

    /// <summary>
    /// Prepares a new, empty vault: archives the vault at the default path if there is one, then makes the default
    /// path the active vault. The vault file itself is created when the user sets a master password.
    /// </summary>
    /// <returns>The archive path of the vault that was moved aside, or null.</returns>
    public string? StartNewVault()
    {
        var archived = ArchiveDefaultVault();
        Activate(_paths.DefaultVaultPath);
        return archived;
    }

    /// <summary>Makes another vault file the active one. The file must exist.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="NetworkVaultException">The file is on a network share and the user has not agreed to that.</exception>
    public void SetActive(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!MayTouch(path))
        {
            throw new NetworkVaultException(path);
        }

        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("That vault file does not exist.", full);
        }

        Activate(full);

        if (!_paths.IsInsideDataFolder(full))
        {
            var settings = _settings.Load();
            if (!settings.KnownVaultPaths.Contains(full, PathComparer))
            {
                settings.KnownVaultPaths.Add(full);
                _settings.Save(settings);
            }
        }
    }

    /// <summary>Stops listing a vault file opened from elsewhere. The file itself is not touched.</summary>
    public void Forget(string path)
    {
        var settings = _settings.Load();
        settings.KnownVaultPaths.RemoveAll(p => Same(p, path));
        if (Same(settings.ActiveVaultPath, path))
        {
            settings.ActiveVaultPath = null;
        }

        _settings.Save(settings);
        if (Same(ActivePath, path))
        {
            _repository.SetPath(_paths.DefaultVaultPath);
        }
    }

    /// <summary>
    /// Permanently deletes a vault file (and its <c>.bak</c>). Only files inside the app's data folder can be
    /// deleted; files opened from elsewhere must be removed by the user.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is outside the app's data folder.</exception>
    public void Delete(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var full = Path.GetFullPath(path);
        if (!_paths.IsInsideDataFolder(full) || !full.EndsWith(".sauth", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only vaults stored in the MasterSafe folder can be deleted here.");
        }

        foreach (var file in new[] { full, full + ".bak", full + ".tmp" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        Forget(full);
    }

    /// <summary>Where the daily copies of the main vault are kept.</summary>
    public string SnapshotFolder => Path.Combine(_paths.DataFolder, "snapshots");

    /// <summary>The daily copies of the main vault, newest first. Empty when another vault file is in use.</summary>
    public IReadOnlyList<SnapshotEntry> ListSnapshots()
    {
        if (!Same(ActivePath, _paths.DefaultVaultPath) || !Directory.Exists(SnapshotFolder))
        {
            return [];
        }

        return Directory.EnumerateFiles(SnapshotFolder, "*.sauth")
            .Where(f => FileVaultRepository.IsSnapshotOf(_paths.DefaultVaultPath, f))
            .Select(f => new FileInfo(f))
            .Where(i => i.LinkTarget is null)
            .OrderByDescending(i => i.Name, StringComparer.Ordinal)
            .Select(i => new SnapshotEntry(i.FullName, i.LastWriteTimeUtc, i.Length))
            .ToList();
    }

    /// <summary>
    /// Puts a daily copy back as the main vault. The vault in use is first copied to the archive, so the restore can be
    /// undone by opening that copy. The snapshot is checked to be a readable vault before anything is replaced, and the
    /// swap is atomic, as in a normal save. The caller must lock the vault first, so no save can overwrite the result.
    /// </summary>
    /// <returns>The archive path of the vault that was in use, or null when there was none.</returns>
    /// <exception cref="InvalidOperationException">The file is not a daily copy of the main vault, or another vault is in use.</exception>
    /// <exception cref="InvalidDataException">The copy is damaged or too large.</exception>
    public async Task<string?> RestoreSnapshotAsync(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshotPath);

        var vault = _paths.DefaultVaultPath;
        var full = Path.GetFullPath(snapshotPath);
        var info = new FileInfo(full);

        // Only a real file straight inside the snapshot folder, named after the main vault, is accepted. A link could
        // point anywhere, and a path from elsewhere is not something the user picked from the list.
        if (!Same(ActivePath, vault) || !Same(info.DirectoryName, SnapshotFolder)
            || !FileVaultRepository.IsSnapshotOf(vault, full) || !info.Exists || info.LinkTarget is not null)
        {
            throw new InvalidOperationException("That file is not a daily copy of this vault.");
        }

        // Throws InvalidDataException when the copy is too large or damaged; then nothing has been touched.
        if (await new FileVaultRepository(full).LoadAsync().ConfigureAwait(false) is null)
        {
            throw new InvalidDataException("The daily copy could not be read.");
        }

        string? kept = null;
        if (File.Exists(vault))
        {
            CreatePrivateDirectory(_paths.ArchiveFolder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            kept = Path.Combine(_paths.ArchiveFolder, $"vault-{stamp}-before-restore.sauth");
            for (var n = 2; File.Exists(kept); n++)
            {
                kept = Path.Combine(_paths.ArchiveFolder, $"vault-{stamp}-before-restore-{n}.sauth");
            }

            File.Copy(vault, kept);
        }

        var temp = vault + ".tmp";
        File.Copy(full, temp, overwrite: true);
        if (File.Exists(vault))
        {
            File.Replace(temp, vault, vault + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, vault);
        }

        Activate(vault);
        return kept;
    }

    private static void CreatePrivateDirectory(string directory)
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

    private void Activate(string path)
    {
        _repository.SetPath(path);

        var settings = _settings.Load();
        settings.ActiveVaultPath = Same(path, _paths.DefaultVaultPath) ? null : path;
        _settings.Save(settings);
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool Same(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
