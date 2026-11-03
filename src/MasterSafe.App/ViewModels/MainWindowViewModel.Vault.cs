// File:    MainWindowViewModel.Vault.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>What is kept inside the vault beside the entries: security settings, the save counter and sync.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Quiet time after a change before an automatic sync.</summary>
    public static readonly TimeSpan SyncQuietTime = TimeSpan.FromMinutes(2);

    /// <summary>The least time between two automatic syncs. A server keeps every file, so they should not pile up.</summary>
    public static readonly TimeSpan SyncMinimumGap = TimeSpan.FromMinutes(15);

    private readonly SyncService _syncService = new(new AesGcmEncryptionService());

    /// <summary>Connects to the place sync files go. Replaced in tests.</summary>
    public Func<SyncSettings, IBackupProvider> SyncProviderFactory { get; set; } = SyncViewModel.CreateProvider;

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private SaveCounterStore? _counters;
    private DebouncedAction? _syncDebounce;
    private DateTimeOffset _lastAutoSync = DateTimeOffset.MinValue;
    private long _saveCounter;
    private long _changes;
    private string? _vaultId;
    private bool _olderCopyNoticed;

    // The vault's security settings win over the settings file. A vault from before 1.6 takes its first values from
    // that file, once, and keeps them from then on.
    private bool ApplyVaultSecurity(VaultContents contents)
    {
        if (contents.Security is { } stored)
        {
            Settings.ApplySecurity(stored);
            return false;
        }

        return true;
    }

    // An older vault copy is worth a warning but never a block: restoring a backup is normal.
    private async Task CheckSaveCounterAsync(VaultContents contents)
    {
        _saveCounter = contents.SaveCounter;
        _olderCopyNoticed = false;
        _vaultId = await _vault!.GetVaultIdAsync();
        if (_counters is null || _vaultId is null)
        {
            return;
        }

        var known = _counters.Get(_vaultId);
        if (known > contents.SaveCounter)
        {
            _olderCopyNoticed = true;
        }

        _saveCounter = Math.Max(known, contents.SaveCounter);
    }

    private void RecordSave()
    {
        if (_counters is not null && _vaultId is not null)
        {
            _counters.Set(_vaultId, _saveCounter);
        }
    }

    private NoticeRow? OlderCopyNotice() => !_olderCopyNoticed
        ? null
        : new NoticeRow(
            "older-copy",
            "This vault is an older copy than the one last used here. If you restored a backup, that is fine. If you did not, someone may have replaced the file, for example to bring back a password you changed.",
            "I understand",
            new RelayCommand(() =>
            {
                _olderCopyNoticed = false;
                _ = PersistAsync();
                RefreshNotices();
            }));

    private void OnSecuritySettingChanged()
    {
        Passwords.RefreshIconSetting();
        Dashboard.HideCodes = Settings.HideCodes;
        AutoHideReveal.HideOnFocusLoss = Settings.HideRevealedOnFocusLoss;
        if (IsUnlocked)
        {
            _ = PersistAsync();
        }
    }

    // ------------------------------------------------------------------ sync

    /// <summary>Called by the sync section with settings to try. Returns whether they work, and what to show.</summary>
    private async Task<(bool Keep, string Message)> RunSyncAsync(SyncSettings settings, bool joining)
    {
        if (_vault is null || _sessionKey is null)
        {
            return (false, Loc.T("Unlock the vault first."));
        }

        if (Passwords.IsFormInUse)
        {
            return (false, Loc.T("Save or clear the entry on the Passwords page first, so sync does not disturb it."));
        }

        await _syncLock.WaitAsync();
        try
        {
            var session = _sessionKey;
            var changesBefore = Volatile.Read(ref _changes);
            var result = await _syncService.RunAsync(BuildContents() with { Sync = settings }, SyncProviderFactory(settings));

            // The vault may have been locked or changed during the wait. What came back must not reach the screens then.
            if (session is null || !ReferenceEquals(session, _sessionKey))
            {
                return (false, Loc.T("The vault was locked, so the sync was not applied. Nothing was lost."));
            }

            if (joining && result.Rejected > 0 && result.Accepted == 0)
            {
                return (false, Loc.T("The files there were not made with this sync code, so nothing was changed. Check the code."));
            }

            var merged = result.Contents;
            if (Volatile.Read(ref _changes) != changesBefore)
            {
                // Something was saved while the sync ran. Merging keeps both.
                (merged, _) = VaultMerger.Merge(BuildContents() with { Sync = merged.Sync }, merged);
            }

            Sync.Settings = merged.Sync;
            if (!result.Summary.NothingChanged)
            {
                ReloadEntries(merged);
            }

            await PersistAsync(fromSync: true);
            return (true, Describe(result));
        }
        catch (Exception ex) when (ex is BackupProviderException or HttpRequestException or IOException or UnauthorizedAccessException or UriFormatException
            or ArgumentException or TaskCanceledException or FormatException)
        {
            return (false, $"{Loc.T("Sync failed. Nothing was lost.")} {ex.Message}");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private string Describe(SyncResult result)
    {
        var parts = new List<string>();
        var s = result.Summary;
        parts.Add(s.NothingChanged ? "Nothing new from other computers." : $"From other computers: {s.Added} added, {s.Updated} updated, {s.Conflicts} changed on both sides, {s.Removed} removed.");
        if (s.Conflicts > 0)
        {
            parts.Add("When an item was changed on two computers, the newer one is used and the older one is kept in its history.");
        }

        parts.Add(result.Uploaded is null && result.UploadError is null ? "No new sync file was needed." : result.Uploaded is not null ? "A new sync file was written." : $"The sync file could not be written: {result.UploadError}");
        if (result.Rejected > 0)
        {
            parts.Add($"{result.Rejected} files could not be opened with this sync code and were ignored.");
        }

        if (result.FilesInPlace >= SyncViewModel.ManyFiles)
        {
            parts.Add($"There are {result.FilesInPlace} sync files there. MasterSafe does not delete files on a server, so remove old ones with the server's own tools.");
        }

        return string.Join(' ', parts.Select(Loc.T));
    }

    private void ReloadEntries(VaultContents contents)
    {
        Dashboard.Load(contents.Accounts, contents.Tombstones);
        Passwords.Load(contents.Passwords, contents.Folders, contents.AttachmentData, contents.FolderIcons, contents.Tombstones);
    }

    private void TriggerAutomaticSync()
    {
        if (Sync.Settings?.Automatic != true)
        {
            return;
        }

        _syncDebounce ??= new DebouncedAction(RecoveryViewModel.StartUiTimer, SyncQuietTime, RunAutomaticSyncAsync);
        _syncDebounce.Trigger();
    }

    private async Task RunAutomaticSyncAsync()
    {
        if (!IsUnlocked || Sync.Settings is not { Automatic: true } settings)
        {
            return;
        }

        if (Passwords.IsFormInUse || _clock() - _lastAutoSync < SyncMinimumGap)
        {
            // Not now: the user is typing, or the last sync was a moment ago.
            _syncDebounce?.Trigger();
            return;
        }

        _lastAutoSync = _clock();
        var (_, message) = await RunSyncAsync(settings, joining: false);
        Sync.Status = message;
    }

    /// <summary>Joins an encrypted backup into the vault. Nothing that is there is replaced; see <see cref="VaultMerger"/>.</summary>
    private async Task<string> MergeBackupAsync(VaultContents backup)
    {
        if (_sessionKey is null)
        {
            return Loc.T("Unlock the vault first.");
        }

        var (merged, summary) = VaultMerger.Merge(BuildContents(), backup);
        ReloadEntries(merged);
        await PersistAsync();
        return Loc.T($"Merged the backup: {summary.Added} added, {summary.Updated} updated, {summary.Conflicts} changed on both sides.");
    }

    /// <summary>Writes the printable emergency sheet to a file the user chooses. It holds no secrets.</summary>
    private EmergencySheetData SheetData(AppServices services)
    {
        var settings = services.SettingsStore.Load();
        return new EmergencySheetData(
            services.Catalog.ActivePath,
            settings.RecoveryKeyId,
            settings.AutoRecoveryBackupFolder,
            Sync.Settings is { } sync ? (sync.UseWebDav ? sync.WebDavUrl : sync.Folder) : null,
            _clock());
    }
}
