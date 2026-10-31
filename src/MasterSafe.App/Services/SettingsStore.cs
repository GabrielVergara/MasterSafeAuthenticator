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

    /// <summary>Wrong master passwords in a row since the last correct one.</summary>
    public int UnlockFailures { get; set; }

    /// <summary>Until when sign-in with the master password is paused (ISO 8601, UTC), or empty.</summary>
    public string? UnlockBlockedUntilUtc { get; set; }

    /// <summary>Lock the vault when Windows is locked or goes to sleep.</summary>
    public bool LockOnSessionLock { get; set; } = true;

    /// <summary>Lock the vault when the window is minimized.</summary>
    public bool LockOnMinimize { get; set; }

    /// <summary>Keep the window out of screenshots and screen sharing (Windows).</summary>
    public bool BlockScreenCapture { get; set; } = true;

    /// <summary>Width of the folder list on the Passwords page in pixels; 0 means fit it to the folder names.</summary>
    public double FolderPaneWidth { get; set; }

    /// <summary>Allow the optional check of passwords against known data breaches (off by default; it contacts api.pwnedpasswords.com).</summary>
    public bool BreachCheckEnabled { get; set; }

    /// <summary>Look for a newer version once after unlocking (off by default; it contacts github.com).</summary>
    public bool CheckForUpdatesOnStart { get; set; }

    /// <summary>Order of the password list (a key such as "title" or "changed").</summary>
    public string? PasswordSort { get; set; }

    /// <summary>Order of the authenticator accounts (a key such as "added" or "name").</summary>
    public string? AccountSort { get; set; }

    /// <summary>Show reminders (recovery key, backup, update) at the top of the codes page.</summary>
    public bool ShowReminders { get; set; } = true;

    /// <summary>Write a recovery backup automatically after changes.</summary>
    public bool AutoRecoveryBackupEnabled { get; set; }

    /// <summary>The folder automatic recovery backups are written to.</summary>
    public string? AutoRecoveryBackupFolder { get; set; }

    /// <summary>Seconds a copied secret stays on the clipboard before it is cleared: 30, 60 or 120.</summary>
    public int ClipboardClearSeconds { get; set; } = ClipboardPolicy.DefaultSeconds;

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
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
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
