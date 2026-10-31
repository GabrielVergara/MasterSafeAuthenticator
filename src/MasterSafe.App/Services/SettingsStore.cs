// File:    SettingsStore.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasterSafe.App.Services;

/// <summary>User settings saved between runs. Holds no secrets: the recovery key is a public key.</summary>
public sealed class AppSettings
{
    /// <summary>Colour theme.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Hide to the tray instead of exiting when the window is closed.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Minutes without activity before the vault locks (1 to 120).</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>The vault file in use. Empty means the default vault.</summary>
    public string? ActiveVaultPath { get; set; }

    /// <summary>Vault files opened from other places, so they can be found again.</summary>
    public List<string> KnownVaultPaths { get; set; } = [];

    /// <summary>The recovery public key (Base64) used to create recovery backups, if one was set up.</summary>
    public string? RecoveryPublicKey { get; set; }

    /// <summary>Identifier of the recovery key (Base64), shown so the right key file can be recognised.</summary>
    public string? RecoveryKeyId { get; set; }

    /// <summary>When the recovery key was created (ISO 8601, UTC).</summary>
    public string? RecoveryKeyCreatedUtc { get; set; }

    /// <summary>When the last recovery backup was written (ISO 8601, UTC).</summary>
    public string? LastRecoveryBackupUtc { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Reads the settings. Missing or damaged files give the defaults.</summary>
    AppSettings Load();

    /// <summary>Writes the settings.</summary>
    void Save(AppSettings settings);
}

/// <summary>Stores settings as JSON, written atomically so a crash cannot leave a half-written file.</summary>
public sealed class SettingsStore(string path) : ISettingsStore
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <inheritdoc />
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes)
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
            return Sanitize(settings);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Sanitize(settings), Options));
        File.Move(temp, path, overwrite: true);
    }

    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.AutoLockMinutes = Math.Clamp(settings.AutoLockMinutes, 1, 120);
        settings.KnownVaultPaths = (settings.KnownVaultPaths ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        if (!string.IsNullOrWhiteSpace(settings.ActiveVaultPath) && !Path.IsPathRooted(settings.ActiveVaultPath))
        {
            settings.ActiveVaultPath = null;
        }

        return settings;
    }
}
