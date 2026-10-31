// File:    AppPaths.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>Where MasterSafe keeps its files on this computer.</summary>
public sealed class AppPaths
{
    /// <summary>Creates the paths under <paramref name="dataFolder"/>.</summary>
    public AppPaths(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);
        DataFolder = Path.GetFullPath(dataFolder);
    }

    /// <summary>
    /// The standard location: <c>%LocalAppData%\MasterSafe</c> on Windows and <c>~/.local/share/MasterSafe</c>
    /// (or <c>$XDG_DATA_HOME/MasterSafe</c>) on Linux.
    /// </summary>
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MasterSafe"));

    /// <summary>Folder holding everything the app stores.</summary>
    public string DataFolder { get; }

    /// <summary>The vault used unless the user opens another file.</summary>
    public string DefaultVaultPath => Path.Combine(DataFolder, "vault.sauth");

    /// <summary>Old vaults moved aside, for example after a lost master password.</summary>
    public string ArchiveFolder => Path.Combine(DataFolder, "archive");

    /// <summary>Saved settings. Contains no secrets.</summary>
    public string SettingsPath => Path.Combine(DataFolder, "settings.json");

    /// <summary>True when <paramref name="path"/> is inside <see cref="DataFolder"/>.</summary>
    public bool IsInsideDataFolder(string path)
    {
        var full = Path.GetFullPath(path);
        var root = DataFolder.EndsWith(Path.DirectorySeparatorChar) ? DataFolder : DataFolder + Path.DirectorySeparatorChar;
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
