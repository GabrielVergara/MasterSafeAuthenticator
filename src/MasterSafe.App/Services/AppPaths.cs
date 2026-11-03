// File:    AppPaths.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>Where MasterSafe keeps its files on this computer.</summary>
public sealed class AppPaths
{
    /// <summary>Creates the paths under <paramref name="dataFolder"/>.</summary>
    /// <param name="dataFolder">The folder for the vault and settings.</param>
    /// <param name="stateFolder">A second folder, for what must not sit beside the vault file. Default: inside the data folder.</param>
    public AppPaths(string dataFolder, string? stateFolder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);
        DataFolder = Path.GetFullPath(dataFolder);
        StateFolder = Path.GetFullPath(stateFolder ?? Path.Combine(dataFolder, "state"));
    }

    /// <summary>
    /// The standard location: <c>%LocalAppData%\MasterSafe</c> on Windows and <c>~/.local/share/MasterSafe</c>
    /// (or <c>$XDG_DATA_HOME/MasterSafe</c>) on Linux.
    /// </summary>
    public static AppPaths Default { get; } = new(
        Path.Combine(SystemFolder(Environment.SpecialFolder.LocalApplicationData), "MasterSafe"),
        Path.Combine(SystemFolder(Environment.SpecialFolder.ApplicationData), "MasterSafe"));

    // Without DoNotVerify the answer is an empty string while the folder does not exist yet, for example ~/.local/share on a
    // new Linux account. The data folder would then depend on where the program was started from: another vault, and
    // another lock, for each place.
    private static string SystemFolder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>Folder holding everything the app stores.</summary>
    public string DataFolder { get; }

    /// <summary>
    /// Holds the copy of the save counter, away from the vault file: <c>%AppData%\MasterSafe</c> on Windows and
    /// <c>~/.config/MasterSafe</c> on Linux.
    /// </summary>
    public string StateFolder { get; }

    /// <summary>The copy of each vault's save counter.</summary>
    public string SaveCounterPath => Path.Combine(StateFolder, "save-counters.json");

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
