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

/// <summary>A vault file the app knows about.</summary>
public sealed record VaultEntry(string Path, string Name, VaultKind Kind, DateTimeOffset Modified, long Size, bool IsActive);

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

        var saved = settings.Load().ActiveVaultPath;
        _repository.SetPath(!string.IsNullOrWhiteSpace(saved) && File.Exists(saved) ? saved : _paths.DefaultVaultPath);
    }

    /// <summary>The vault file in use.</summary>
    public string ActivePath => _repository.Path;

    /// <summary>All vault files the app knows about, the active one first.</summary>
    public IReadOnlyList<VaultEntry> List()
    {
        var entries = new Dictionary<string, VaultEntry>(PathComparer);

        void Add(string path, VaultKind kind)
        {
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

        Directory.CreateDirectory(_paths.ArchiveFolder);
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
    public void SetActive(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

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
