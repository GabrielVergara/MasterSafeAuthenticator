// File:    RecoveryViewModel.Auto.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;

namespace MasterSafe.App.ViewModels;

/// <summary>Automatic recovery backups, and how old the last backup is.</summary>
public sealed partial class RecoveryViewModel
{
    private DebouncedAction? _debounce;
    private DateTimeOffset _lastAuto = DateTimeOffset.MinValue;

    /// <summary>True while the vault is unlocked. Backups are never written otherwise.</summary>
    public Func<bool>? IsVaultUnlocked { get; set; }

    /// <summary>Starts a one-shot timer. Replaced in tests; by default the app's UI timer.</summary>
    public Func<TimeSpan, Func<Task>, IDisposable>? TimerFactory { get; set; }

    /// <summary>The current time. Replaced in tests.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Whether a recovery backup is written automatically after changes.</summary>
    [ObservableProperty]
    private bool _autoBackupEnabled;

    /// <summary>The folder automatic backups are written to.</summary>
    [ObservableProperty]
    private string _autoBackupFolder = string.Empty;

    /// <summary>Result of the last automatic backup.</summary>
    [ObservableProperty]
    private string? _autoBackupStatus;

    /// <summary>How many days ago the last recovery backup was made; null when there is none.</summary>
    public int? DaysSinceLastBackup { get; private set; }

    private bool _loadingAuto;

    private void LoadAutoSettings(Services.AppSettings? settings)
    {
        _loadingAuto = true;
        AutoBackupEnabled = settings?.AutoRecoveryBackupEnabled == true && !string.IsNullOrEmpty(settings.AutoRecoveryBackupFolder);
        AutoBackupFolder = settings?.AutoRecoveryBackupFolder ?? string.Empty;
        _loadingAuto = false;

        DaysSinceLastBackup = DateTimeOffset.TryParse(settings?.LastRecoveryBackupUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var last)
            ? Math.Max(0, (int)(Clock() - last).TotalDays)
            : null;
        OnPropertyChanged(nameof(DaysSinceLastBackup));
    }

    partial void OnAutoBackupEnabledChanged(bool value)
    {
        if (_loadingAuto)
        {
            return;
        }

        if (value && string.IsNullOrEmpty(AutoBackupFolder))
        {
            // A folder is needed first; choosing one switches the backup on.
            _loadingAuto = true;
            AutoBackupEnabled = false;
            _loadingAuto = false;
            _ = ChooseAutoBackupFolderAsync();
            return;
        }

        SaveAutoSettings();
        AutoBackupStatus = value
            ? "Automatic backup is on. A new recovery backup is written a minute after you change something."
            : "Automatic backup is off.";
        if (!value)
        {
            _debounce?.Cancel();
        }
    }

    /// <summary>Lets the user choose where automatic backups go, and switches them on.</summary>
    [RelayCommand]
    private async Task ChooseAutoBackupFolderAsync()
    {
        if (_services is null)
        {
            return;
        }

        var folder = await _services.Dialogs.PickFolderAsync("Choose where automatic recovery backups are saved");
        if (folder is null)
        {
            return;
        }

        AutoBackupFolder = folder;
        _loadingAuto = true;
        AutoBackupEnabled = true;
        _loadingAuto = false;
        SaveAutoSettings();
        AutoBackupStatus = $"Automatic backup is on. The newest {AutoRecoveryBackup.KeepNewest} backups are kept in {folder}.";
    }

    private void SaveAutoSettings()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var settings = _services.SettingsStore.Load();
            settings.AutoRecoveryBackupEnabled = AutoBackupEnabled;
            settings.AutoRecoveryBackupFolder = string.IsNullOrEmpty(AutoBackupFolder) ? null : AutoBackupFolder;
            _services.SettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AutoBackupStatus = $"The setting could not be saved: {ex.Message}";
        }
    }

    /// <summary>Called after the vault was saved. Starts the wait for an automatic backup when it is switched on.</summary>
    public void NotifyVaultChanged()
    {
        if (!AutoBackupEnabled || !HasKey || _services is null || string.IsNullOrEmpty(AutoBackupFolder))
        {
            return;
        }

        _debounce ??= new DebouncedAction(TimerFactory ?? StartUiTimer, AutoRecoveryBackup.QuietTime, RunAutoBackupAsync);
        _debounce.Trigger();
    }

    /// <summary>Forgets a waiting backup (used when the vault is locked).</summary>
    public void CancelAutoBackup() => _debounce?.Cancel();

    /// <summary>
    /// Writes an automatic recovery backup now, unless the vault is locked or empty, or the last one was written a moment
    /// ago. Older backups beyond the newest few are removed afterwards.
    /// </summary>
    public async Task RunAutoBackupAsync()
    {
        if (_services is null || _contents is null || !AutoBackupEnabled || string.IsNullOrEmpty(AutoBackupFolder))
        {
            return;
        }

        if (IsVaultUnlocked?.Invoke() == false)
        {
            return;
        }

        if (Clock() - _lastAuto < AutoRecoveryBackup.MinimumGap)
        {
            // Too soon after the last one: try again later.
            _debounce?.Trigger();
            return;
        }

        var settings = _services.SettingsStore.Load();
        if (string.IsNullOrEmpty(settings.RecoveryPublicKey))
        {
            return;
        }

        try
        {
            var contents = _contents();
            if (contents.Accounts.Count == 0 && contents.Passwords.Count == 0)
            {
                // Never let an empty vault push real backups out of the folder.
                return;
            }

            Directory.CreateDirectory(AutoBackupFolder);
            await _services.RecoveryBackups.ExportContentsAsync(
                contents, Convert.FromBase64String(settings.RecoveryPublicKey), new LocalFolderBackupProvider(AutoBackupFolder));
            _lastAuto = Clock();

            settings = _services.SettingsStore.Load();
            settings.LastRecoveryBackupUtc = Clock().ToString("O", CultureInfo.InvariantCulture);
            _services.SettingsStore.Save(settings);

            AutoRecoveryBackup.Prune(AutoBackupFolder);
            AutoBackupStatus = $"Automatic recovery backup saved at {Clock().ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}.";
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or PlatformNotSupportedException)
        {
            AutoBackupStatus = $"The automatic backup failed: {ex.Message}";
        }
    }

    private static IDisposable StartUiTimer(TimeSpan interval, Func<Task> action)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return new NoTimer();
        }

        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await action();
        };
        timer.Start();
        return new StopTimer(timer);
    }

    private sealed class NoTimer : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class StopTimer(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
