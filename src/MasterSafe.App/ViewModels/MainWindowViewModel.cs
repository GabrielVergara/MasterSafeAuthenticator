// File:    MainWindowViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System;
using System.Security.Cryptography;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Root view model: owns navigation, the unlocked vault session, unlock methods, auto-lock and persistence.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IVaultService? _vault;
    private readonly IReadOnlyList<ISecretProvider> _providers;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly DispatcherTimer? _idleTimer;
    private readonly DispatcherTimer? _lockoutTimer;
    private readonly UnlockThrottle _throttle;
    private byte[]? _sessionKey;
    private DateTimeOffset _lastActivity;
    private long _lastActivityTicks;
    private readonly ElapsedClock _elapsed;

    // The sign-in counts the open vault remembers, per computer, and this computer's key in that list.
    private Dictionary<string, long> _failuresSeen = new(StringComparer.Ordinal);
    private readonly string _computerKey;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private object _currentPage;

    /// <summary>Name of the navigation section matching <see cref="CurrentPage"/> (drives the sidebar highlight).</summary>
    [ObservableProperty]
    private string _activeSection = string.Empty;

    partial void OnCurrentPageChanged(object value)
    {
        // A sync code on the screen is a key. It does not stay there when the user moves on.
        Sync.HideCodeCommand.Execute(null);

        if (value is DashboardViewModel)
        {
            Recovery.Refresh();
            RefreshNotices();
        }
        else if (value is GuideViewModel guide)
        {
            Recovery.Refresh();
            guide.Refresh();
        }

        OnPropertyChanged(nameof(IsLockedHelp));
        ActiveSection = value switch
        {
            DashboardViewModel => "Dashboard",
            PasswordsViewModel => "Passwords",
            HealthViewModel => "Health",
            AccountEditorViewModel => "Add",
            ImportViewModel => "Import",
            BackupViewModel => "Backup",
            VaultsViewModel => "Vaults",
            RecoveryViewModel => "Recovery",
            SettingsViewModel => "Settings",
            HelpViewModel => "Help",
            _ => string.Empty,
        };
    }

    /// <summary>The size factor of the whole window (1.0 to 2.0), from the text size setting.</summary>
    public double TextScale => Settings.TextScalePercent / 100.0;

    /// <summary>Seconds left before the vault locks by itself, shown as a warning; null when there is no warning.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLockWarningVisible), nameof(LockWarningText))]
    private int? _lockWarningSeconds;

    /// <summary>The time before the automatic lock at which the warning appears.</summary>
    public static readonly TimeSpan LockWarningTime = TimeSpan.FromSeconds(30);

    /// <summary>True while the warning about the coming automatic lock is shown.</summary>
    public bool IsLockWarningVisible => LockWarningSeconds is not null;

    /// <summary>For example "The vault locks in 20 seconds because nothing was touched."</summary>
    public string LockWarningText => LockWarningSeconds is { } s
        ? $"The vault locks in {s} {(s == 1 ? "second" : "seconds")} because nothing was touched."
        : string.Empty;

    /// <summary>Keeps the vault unlocked: counts as activity and hides the warning.</summary>
    [RelayCommand]
    private void KeepUnlocked()
    {
        NotifyActivity();
        LockWarningSeconds = null;
    }

    /// <summary>Locks the vault now (shortcut Ctrl+L).</summary>
    [RelayCommand]
    private void LockShortcut() => LockNow();

    /// <summary>Closes the program. Set by the application.</summary>
    public Action? ExitRequested
    {
        get => Login.ExitRequested;
        set => Login.ExitRequested = value;
    }

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke();

    /// <summary>
    /// Opens the help (shortcut F1). While the vault is locked the help can be read from the sign-in screen too: it holds
    /// no vault data, and F1 again goes back to sign in.
    /// </summary>
    [RelayCommand]
    private void HelpShortcut()
    {
        if (IsUnlocked)
        {
            CurrentPage = Help;
        }
        else
        {
            CurrentPage = CurrentPage is LoginViewModel ? Help : Login;
        }
    }

    /// <summary>True while the help is read from the sign-in screen, so it shows a way back.</summary>
    public bool IsLockedHelp => !IsUnlocked && CurrentPage is HelpViewModel;

    /// <summary>Leaves the help opened from the sign-in screen.</summary>
    [RelayCommand]
    private void BackToSignIn()
    {
        if (!IsUnlocked)
        {
            CurrentPage = Login;
        }
    }

    partial void OnIsUnlockedChanged(bool value) => OnPropertyChanged(nameof(IsLockedHelp));

    /// <summary>Makes the text and the window larger (Ctrl and plus).</summary>
    [RelayCommand]
    private void ZoomIn() => Settings.TextScalePercent = StepScale(+1);

    /// <summary>Makes the text and the window smaller (Ctrl and minus).</summary>
    [RelayCommand]
    private void ZoomOut() => Settings.TextScalePercent = StepScale(-1);

    /// <summary>Goes back to the normal size (Ctrl and 0).</summary>
    [RelayCommand]
    private void ZoomReset() => Settings.TextScalePercent = 100;

    private int StepScale(int direction)
    {
        var options = SettingsViewModel.TextScaleOptions;
        var index = options.ToList().IndexOf(SettingsViewModel.NearestTextScale(Settings.TextScalePercent));
        return options[Math.Clamp(index + direction, 0, options.Count - 1)];
    }

    public LoginViewModel Login { get; } = new();

    public DashboardViewModel Dashboard { get; }

    public PasswordsViewModel Passwords { get; } = new();

    /// <summary>The password health page.</summary>
    public HealthViewModel Health { get; }

    /// <summary>The first-run guide, shown once after a new vault is created.</summary>
    public GuideViewModel Guide { get; }

    private bool _showGuideNext;

    public SettingsViewModel Settings { get; }

    public VaultsViewModel Vaults { get; }

    public RecoveryViewModel Recovery { get; }

    public BackupViewModel Backup { get; } = new();

    /// <summary>Recent security events, shown in Settings.</summary>
    public SecurityEventsViewModel SecurityLog { get; } = new();

    /// <summary>Sync between computers, on the Backup page.</summary>
    public SyncViewModel Sync => Backup.Sync;

    public ImportViewModel Import { get; } = new();

    public AccountEditorViewModel AccountEditor { get; } = new();

    public HelpViewModel Help { get; } = new();

    /// <summary>Raised when the user asks to open the About dialog.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>
    /// Creates the shell. With no vault service a demo account is shown (design/test mode).
    /// </summary>
    /// <param name="vault">The vault, or null for demo mode.</param>
    /// <param name="providers">Hardware or OS unlock methods offered on this platform.</param>
    /// <param name="clock">Time source (replaced in tests).</param>
    /// <param name="startIdleTimer">Starts the auto-lock timer; needs the UI thread.</param>
    /// <param name="services">Vault management, settings and recovery services; null in design and test mode.</param>
    public MainWindowViewModel(
        IVaultService? vault = null,
        IEnumerable<ISecretProvider>? providers = null,
        Func<DateTimeOffset>? clock = null,
        bool startIdleTimer = false,
        AppServices? services = null)
    {
        _vault = vault;
        _providers = providers?.ToList() ?? [];
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _elapsed = clock is null ? ElapsedClock.System : ElapsedClock.From(clock);
        _lastActivity = _clock();
        _lastActivityTicks = _elapsed.Now();
        Dashboard = new DashboardViewModel();
        Settings = new SettingsViewModel(services?.SettingsStore);
        _throttle = new UnlockThrottle(_clock, services?.SettingsStore, _elapsed);
        _computerKey = ComputerKey(services?.SettingsStore);
        _counters = services is null ? null : new SaveCounterStore(services.Paths.SaveCounterPath);
        Settings.SecurityChanged += (_, _) => OnSecuritySettingChanged();
        Settings.SecurityApplied += (_, _) =>
        {
            Passwords.RefreshIconSetting();
            Dashboard.HideCodes = Settings.HideCodes;
            AutoHideReveal.HideOnFocusLoss = Settings.HideRevealedOnFocusLoss;
        };
        Dashboard.HideCodes = Settings.HideCodes;
        AutoHideReveal.HideOnFocusLoss = Settings.HideRevealedOnFocusLoss;
        Passwords.IconsAllowed = () => Settings.WebsiteIconsEnabled;
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.TextScalePercent))
            {
                OnPropertyChanged(nameof(TextScale));
            }
        };
        Settings.Updates = new UpdatesViewModel(
            AppInfo.UpdatedBy is null ? TryCreateUpdateChecker() : null,
            services?.Dialogs,
            updatedBy: AppInfo.UpdatedBy);
        Settings.Updates.UpdateFound += (_, info) =>
        {
            _newerRelease = info;
            if (IsUnlocked)
            {
                Dashboard.StatusMessage = $"Version {info.Version} is available. Open Settings to download and verify it.";
                RefreshNotices();
            }
        };
        Health = new HealthViewModel(() => Passwords.Snapshot(), () => Settings.BreachCheckEnabled, new BreachChecker(BreachChecker.CreateClient()), _clock,
            () => new CheckupInput(
                Settings.AutoLockMinutes, Settings.LockOnSessionLock, Settings.BlockScreenCapture,
                Settings.SelectedClipboardDelay.Seconds, Recovery!.HasKey, Recovery.AutoBackupEnabled,
                Settings.BreachCheckEnabled, Settings.UnlockMethods.Count))
        {
            OpenEntry = id =>
            {
                Passwords.EditEntryById(id);
                CurrentPage = Passwords;
            },
            MoveToBin = id => Passwords.MoveToBinById(id),
            UseHttps = id => Passwords.UseHttpsById(id),
        };
        Vaults = new VaultsViewModel(services, _providers);
        Recovery = new RecoveryViewModel(services, BuildContents) { SaveRequested = PersistAsync };
        Recovery.IsVaultUnlocked = () => IsUnlocked;
        SecurityLog.Changed += (_, _) => _ = PersistAsync();
        Settings.Events = SecurityLog;
        Backup.Exported = what => SecurityLog.Record(SecurityEventsViewModel.Exported, what);
        Passwords.FileSaved = () => SecurityLog.Record(SecurityEventsViewModel.FileSaved);
        Recovery.Clock = _clock;
        Recovery.SheetData = services is null ? null : () => SheetData(services);
        Login.Forgot = new ForgotPasswordViewModel(services, vault);
        Login.HelpRequested = () => HelpShortcut();
        Login.AboutRequested = () => ShowAbout();
        _currentPage = Login;

        if (_vault is null)
        {
            Login.UnlockRequested += (_, _) => Unlock();
            Dashboard.Load(
            [
                new OtpAccount { Issuer = "MasterSafe", Label = "demo@local", Secret = "JBSWY3DPEHPK3PXP" },
            ]);
        }
        else
        {
            Login.UnlockRequested += async (_, password) => await UnlockVaultAsync(password);
            Login.MethodUnlockRequested += async (_, provider) => await UnlockWithMethodAsync(provider);
            _ = InitializeLoginAsync();
        }

        Backup.AccountsProvider = () => Dashboard.Accounts.Select(a => a.Account).ToList();
        Backup.ContentsProvider = BuildContents;
        Backup.MergeRequested = MergeBackupAsync;
        Backup.Dialogs = services?.Dialogs;
        Sync.Dialogs = services?.Dialogs;
        Sync.RunRequested = RunSyncAsync;
        Sync.SettingsChanged = _ => PersistAsync();
        if (services?.Catalog.RefusedNetworkPath is { } refused)
        {
            Login.RefusedPath = refused;
            Login.DismissRefusedPath = () =>
            {
                services.Catalog.DismissNetworkPath();
                Login.RefusedPath = null;
            };
            Login.OpenRefusedPath = async () =>
            {
                Login.RefusedPath = null;
                services.Catalog.AllowNetworkPath(refused);
                try
                {
                    services.Catalog.SetActive(refused);
                    await OnActiveVaultChangedAsync();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Login.Error = $"Could not open vault: {ex.Message}";
                }
            };
        }

        Dashboard.AccountsChanged += async (_, _) => await PersistAsync();
        Passwords.EntriesChanged += async (_, _) => await PersistAsync();
        Import.Dialogs = services?.Dialogs;
        Import.KeePass.Dialogs = services?.Dialogs;
        Passwords.Dialogs = services?.Dialogs;
        if (services is not null)
        {
            var saved = services.SettingsStore.Load();
            Passwords.RestoreFolderPaneWidth(saved.FolderPaneWidth);
            Passwords.RestoreSort(saved.PasswordSort);
            Dashboard.RestoreSort(saved.AccountSort);
            Passwords.SortChanged += (_, _) => SaveSortChoices(services);
            Dashboard.SortChanged += (_, _) => SaveSortChoices(services);
            Passwords.FolderPaneWidthChanged += (_, _) =>
            {
                try
                {
                    var settings = services.SettingsStore.Load();
                    settings.FolderPaneWidth = Passwords.UserFolderPaneWidth ?? 0;
                    services.SettingsStore.Save(settings);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The width is a convenience; it is simply not remembered.
                }
            };
        }
        Import.KeePass.ExistingPasswords = () => Passwords.Snapshot();
        Import.KeePass.ExistingAccounts = () => Dashboard.Accounts.Select(a => a.Account).ToList();
        Import.KeePass.Confirmed += (_, e) =>
        {
            var dropped = Passwords.AddRange(e.Passwords, e.Attachments);
            Dashboard.AddRange(e.Accounts);
            Dashboard.StatusMessage = dropped == 0
                ? Import.KeePass.Status
                : $"{Import.KeePass.Status} {dropped} attachments were left out because the vault's attachment limit was reached.";
            CurrentPage = e.Passwords.Count > 0 ? Passwords : Dashboard;
        };
        Import.IsDuplicate = account => Dashboard.Accounts.Any(a => SameAccount(a.Account, account));
        Import.AccountImported += (_, account) =>
        {
            Dashboard.Add(account);
            Dashboard.StatusMessage = Import.Status;
            CurrentPage = Dashboard;
        };
        AccountEditor.AccountSaved += (_, account) =>
        {
            Dashboard.Add(account);
            CurrentPage = Dashboard;
        };
        Vaults.ActiveVaultChanged = OnActiveVaultChangedAsync;
        Vaults.BeforeVaultReplaced = async () =>
        {
            // A save still running would write over the restored file, so wait for it, then lock.
            await _saveLock.WaitAsync();
            try
            {
                Lock();
            }
            finally
            {
                _saveLock.Release();
            }
        };
        Login.Forgot.ActiveVaultChanged = OnActiveVaultChangedAsync;
        Login.Forgot.Restored = async password =>
        {
            await InitializeLoginAsync();
            await UnlockVaultAsync(password);
        };
        Guide = new GuideViewModel(GuideSteps, () => CurrentPage = Dashboard);
        Settings.ChangePasswordHandler = (current, next) => ChangeMasterPasswordAsync(current, next);
        Settings.StrengthenHandler = StrengthenProtectionAsync;
        Settings.AddMethodHandler = AddUnlockMethodAsync;
        Settings.RemoveMethodHandler = RemoveUnlockMethodAsync;

        if (startIdleTimer && Dispatcher.UIThread.CheckAccess())
        {
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _idleTimer.Tick += (_, _) => LockIfIdle();
            _idleTimer.Start();

            _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _lockoutTimer.Tick += (_, _) => UpdateLockout();
        }

        UpdateLockout();
    }

    /// <summary>
    /// Imports QR code images dropped on the window. Only works while the vault is unlocked. Returns the number of
    /// accounts added. When nothing could be imported the Import page is shown so the reason is visible.
    /// </summary>
    public int ImportDroppedFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!IsUnlocked)
        {
            return 0;
        }

        NotifyActivity();

        var files = paths.ToList();
        if (files.Count == 1 && Path.GetExtension(files[0]).ToLowerInvariant() is ".xml" or ".csv" or ".kdbx" or ".json" or ".1pux" or ".2fas" or ".zip")
        {
            Import.KeePass.Load(files[0]);
            CurrentPage = Import;
            return 0;
        }

        var imported = Import.ImportFromImages(files);
        if (imported == 0)
        {
            CurrentPage = Import;
        }

        return imported;
    }

    private static bool SameAccount(OtpAccount a, OtpAccount b)
        => string.Equals(Normalise(a.Secret), Normalise(b.Secret), StringComparison.Ordinal)
           && string.Equals(a.Issuer, b.Issuer, StringComparison.OrdinalIgnoreCase)
           && string.Equals(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string secret)
        => secret.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Records user activity so the auto-lock countdown restarts.</summary>
    public void NotifyActivity()
    {
        _lastActivity = _clock();
        _lastActivityTicks = _elapsed.Now();
    }

    /// <summary>Locks the vault when there has been no activity for the configured time.</summary>
    public void LockIfIdle()
    {
        if (!IsUnlocked || _vault is null)
        {
            LockWarningSeconds = null;
            return;
        }

        // The longer of the two: a clock turned back cannot keep the vault open, and a computer that slept (where the tick
        // count may stop, on Linux) still locks by the clock.
        var idle = _elapsed.Since(_lastActivityTicks);
        var byClock = _clock() - _lastActivity;
        var remaining = TimeSpan.FromMinutes(Math.Max(1, Settings.AutoLockMinutes)) - (byClock > idle ? byClock : idle);
        if (remaining <= TimeSpan.Zero)
        {
            LockWarningSeconds = null;
            Lock();
            return;
        }

        // A warning a little before the lock gives slower readers, and people using a screen reader, time to react.
        LockWarningSeconds = remaining <= LockWarningTime ? (int)Math.Ceiling(remaining.TotalSeconds) : null;
    }

    /// <summary>Locks the vault now (for example when Windows locks). Does nothing when it is already locked.</summary>
    public void LockNow()
    {
        if (IsUnlocked)
        {
            Lock();
        }
    }

    /// <summary>Removes a copied secret from the clipboard. Set by the application.</summary>
    public Func<Task>? ClearClipboard { get; set; }

    /// <summary>Shows or clears the pause after too many wrong passwords, and keeps the countdown going.</summary>
    public void UpdateLockout()
    {
        var blocked = _throttle.IsBlocked;
        Login.IsLockedOut = blocked;
        Login.LockoutMessage = blocked
            ? $"Too many wrong passwords. Try again in {UnlockThrottle.Describe(_throttle.Remaining)}."
            : null;

        if (blocked)
        {
            _lockoutTimer?.Start();
        }
        else
        {
            _lockoutTimer?.Stop();
        }
    }

    private static UpdateChecker? TryCreateUpdateChecker()
    {
        try
        {
            return new UpdateChecker(UpdateChecker.CreateClient(), ReleaseKey.Load(), AppInfo.Version);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private void SaveSortChoices(AppServices services)
    {
        try
        {
            var settings = services.SettingsStore.Load();
            settings.PasswordSort = Passwords.SelectedSort.Key;
            settings.AccountSort = Dashboard.SelectedSort.Key;
            services.SettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The order is a convenience; it is simply not remembered.
        }
    }

    private UpdateInfo? _newerRelease;

    /// <summary>Rebuilds the reminders at the top of the codes page.</summary>
    public void RefreshNotices()
    {
        // This one is a warning, not a reminder, so the setting does not hide it.
        var olderCopy = IsUnlocked ? OlderCopyNotice() : null;
        if (!IsUnlocked || !Settings.ShowReminders)
        {
            Dashboard.SetNotices(olderCopy is null ? [] : [olderCopy]);
            return;
        }

        var notices = new List<NoticeRow>();
        var expiring = PasswordHealth.CountExpiring(Passwords.Snapshot(), _clock());
        if (expiring > 0)
        {
            notices.Add(new NoticeRow(
                "expiring",
                expiring == 1 ? "1 item has reached its expiry date, or will soon." : $"{expiring} items have reached their expiry date, or will soon.",
                "Show them",
                ShowHealthCommand));
        }

        if (_newerRelease is not null)
        {
            notices.Add(new NoticeRow(
                "update",
                $"Version {_newerRelease.Version} is available.",
                "Open Settings",
                new RelayCommand(() => CurrentPage = Settings)));
        }

        if (Recovery.IsAvailable && Recovery.IsSupported && !Recovery.HasKey)
        {
            notices.Add(new NoticeRow(
                "recovery-key",
                "You have no recovery key yet. If you forget your master password, your data cannot be recovered.",
                "Set up recovery",
                new RelayCommand(() => CurrentPage = Recovery)));
        }
        else if (Recovery.IsAvailable && Recovery.HasKey && (Recovery.DaysSinceLastBackup is null or > 30))
        {
            notices.Add(new NoticeRow(
                "recovery-backup",
                Recovery.DaysSinceLastBackup is null
                    ? "You have a recovery key but no recovery backup yet."
                    : $"Your last recovery backup is {Recovery.DaysSinceLastBackup} days old.",
                "Create a backup",
                new RelayCommand(() => CurrentPage = Recovery)));
        }

        if (Login.UnlockMethods.Count == 0 && Settings.AddableMethods.Count > 0)
        {
            notices.Add(new NoticeRow(
                "unlock-method",
                "You can unlock faster, and more safely, with Windows Hello or a security key.",
                "Add one",
                new RelayCommand(() => CurrentPage = Settings)));
        }

        if (!Settings.CheckForUpdatesOnStart && Settings.Updates?.UpdatesItself != false)
        {
            notices.Add(new NoticeRow(
                "updates",
                "MasterSafe does not look for new versions by itself, so you would not hear about security fixes.",
                "Turn on",
                new RelayCommand(() =>
                {
                    Settings.CheckForUpdatesOnStart = true;
                    RefreshNotices();
                })));
        }

        if (olderCopy is not null)
        {
            notices.Insert(0, olderCopy);
        }

        Dashboard.SetNotices(notices);
    }

    private bool _updateChecked;

    /// <summary>Looks for a newer version once per run, when the user allowed it.</summary>
    private void CheckForUpdatesOnce()
    {
        if (_updateChecked || !Settings.CheckForUpdatesOnStart || Settings.Updates is not { UpdatesItself: true })
        {
            return;
        }

        _updateChecked = true;
        _ = Settings.Updates.CheckAsync(quiet: true);
    }

    /// <summary>Resets the failure counter after a correct unlock and tells the user about any wrong attempts before it.</summary>
    private void AfterSuccessfulUnlock(string method)
    {
        // Wrong passwords on this computer since this vault was last opened here. The vault's own record cannot be edited
        // without the password, so a counter turned back in the settings file is noticed.
        var total = _throttle.TotalFailures;
        var seen = _failuresSeen.GetValueOrDefault(_computerKey);
        var turnedBack = total < seen;
        var failed = (int)Math.Min(1000, Math.Max(_throttle.RecordSuccess(), turnedBack ? 0 : total - seen));
        _failuresSeen[_computerKey] = total;
        if (failed > 0)
        {
            SecurityLog.Record(SecurityEventsViewModel.WrongPasswords, failed.ToString(System.Globalization.CultureInfo.InvariantCulture), save: false);
        }

        if (turnedBack)
        {
            SecurityLog.Record(SecurityEventsViewModel.SignInCounterChanged, string.Empty, save: false);
        }

        // Saved by the unlock itself, which waits for it, so it never races another save.
        SecurityLog.Record(SecurityEventsViewModel.Unlocked, method, save: false);
        UpdateLockout();
        CheckForUpdatesOnce();
        if (turnedBack)
        {
            Dashboard.StatusMessage = "The count of wrong master passwords on this computer was turned back outside MasterSafe. If you did not do that, someone may have tried to guess your password: change it.";
        }
        else if (failed > 0)
        {
            Dashboard.StatusMessage = failed == 1
                ? "1 wrong password was entered since your last sign-in."
                : $"{failed} wrong passwords were entered since your last sign-in. If that was not you, change your master password.";
        }
    }

    /// <summary>Creates (first run) or opens the vault with the master password and shows the dashboard.</summary>
    public async Task UnlockVaultAsync(string password)
    {
        if (_throttle.IsBlocked)
        {
            UpdateLockout();
            return;
        }

        _showGuideNext = Login.IsCreateMode;
        Login.IsBusy = true;
        try
        {
            if (!await _vault!.ExistsAsync())
            {
                await _vault.CreateAsync(password);
            }

            var key = await _vault.DeriveKeyAsync(password);
            await CompleteUnlockWithKeyAsync(key);
            AfterSuccessfulUnlock("master password");
            await PersistAsync();
        }
        catch (InvalidPasswordException)
        {
            _throttle.RecordFailure();
            UpdateLockout();
            Login.Error = _throttle.IsBlocked
                ? null
                : _throttle.AttemptsLeft > 0
                    ? $"Incorrect master password. {_throttle.AttemptsLeft} {(_throttle.AttemptsLeft == 1 ? "try" : "tries")} left before sign-in pauses."
                    : "Incorrect master password.";
        }
        catch (Exception ex)
        {
            Login.Error = $"Could not open vault: {ex.Message}";
        }
        finally
        {
            Login.IsBusy = false;
        }
    }

    /// <summary>Opens the vault with a hardware or OS unlock method such as Windows Hello.</summary>
    public async Task UnlockWithMethodAsync(ISecretProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Login.IsBusy = true;
        try
        {
            var key = await _vault!.UnlockWithProviderAsync(provider);
            await CompleteUnlockWithKeyAsync(key);
            AfterSuccessfulUnlock(provider.DisplayName);
            await PersistAsync();
        }
        catch (InvalidPasswordException)
        {
            Login.Error = $"{provider.DisplayName} could not unlock the vault. Use your master password.";
        }
        catch (OperationCanceledException)
        {
            Login.Error = $"{provider.DisplayName} was cancelled.";
        }
        catch (Exception ex)
        {
            Login.Error = $"{provider.DisplayName} failed: {ex.Message}";
        }
        finally
        {
            Login.IsBusy = false;
        }
    }

    /// <summary>Saves accounts and passwords to the encrypted vault (no-op without a vault or session).</summary>
    public Task PersistAsync() => PersistAsync(fromSync: false);

    private async Task PersistAsync(bool fromSync)
    {
        if (_vault is null || _sessionKey is null)
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            if (_sessionKey is not null)
            {
                _saveCounter++;
                await _vault.SaveWithKeyAsync(_sessionKey, BuildContents());
                RecordSave();
                Interlocked.Increment(ref _changes);
                Recovery.NotifyVaultChanged();
                if (!fromSync)
                {
                    TriggerAutomaticSync();
                }
            }
        }
        catch (Exception ex)
        {
            Dashboard.StatusMessage = $"Could not save vault: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Changes the master password. Returns null on success or an error message.
    /// Other unlock methods (Windows Hello, security keys) keep working.
    /// </summary>
    /// <summary>
    /// Tests this computer and, when it can afford stronger Argon2id settings than the vault has, re-encrypts the vault
    /// key under them. The password stays the same. Returns the message to show.
    /// </summary>
    public async Task<string> StrengthenProtectionAsync(string password)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        Argon2Parameters calibrated;
        try
        {
            calibrated = await Task.Run(() => Argon2Calibrator.Calibrate());
        }
        catch (Exception ex) when (ex is OutOfMemoryException or InvalidOperationException)
        {
            return Loc.T("This computer does not have enough free memory for the test. Your vault was not changed.");
        }

        var slots = await _vault.ListSlotsAsync();
        var current = slots.FirstOrDefault(s => s.Type == SlotTypes.Password)?.Kdf;
        if (!Argon2Calibrator.IsWorthChanging(current, calibrated))
        {
            return "Your vault already uses the strongest settings this computer can handle comfortably.";
        }

        var error = await ChangeMasterPasswordAsync(password, password, calibrated);
        if (error is not null)
        {
            return error;
        }

        await RefreshUnlockMethodsAsync();
        return $"Done. The vault now uses {calibrated.MemorySizeKib / 1024} MiB and {calibrated.Iterations} passes. Unlocking takes about a second.";
    }

    public async Task<string?> ChangeMasterPasswordAsync(string currentPassword, string newPassword, Argon2Parameters? kdf = null)
        => await SaveEventsAfterAsync(await ChangeMasterPasswordCoreAsync(currentPassword, newPassword, kdf));

    private async Task<string?> ChangeMasterPasswordCoreAsync(string currentPassword, string newPassword, Argon2Parameters? kdf)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        await _saveLock.WaitAsync();
        try
        {
            // A real change also replaces the data key, so a leaked old key or an old copy with the old password reads
            // nothing saved from now on. Strengthening keeps the same password and key, and so the hardware methods.
            var newDataKey = !string.Equals(currentPassword, newPassword, StringComparison.Ordinal);
            var rotation = await _vault.RotateMasterPasswordAsync(currentPassword, newPassword, kdf, newDataKey);
            var old = _sessionKey;
            _sessionKey = rotation.DataKey;
            CryptographicOperations.ZeroMemory(old);

            foreach (var slot in rotation.RemovedSlots)
            {
                var provider = _providers.FirstOrDefault(p => p.SlotType == slot.Type);
                if (provider is not null)
                {
                    try
                    {
                        await provider.RemoveAsync(slot.Id);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        // The slot is gone from the vault; a leftover credential can only open old copies.
                    }
                }
            }

            if (newDataKey)
            {
                SecurityLog.Record(SecurityEventsViewModel.PasswordChanged, save: false);
            }

            if (rotation.RemovedSlots.Count > 0)
            {
                await RefreshUnlockMethodsAsync();
                Settings.UnlockMethodStatus = Loc.T("The vault now has a new encryption key, so Windows Hello and security keys were removed. Add them again below.");
            }

            return null;
        }
        catch (InvalidPasswordException)
        {
            return "Current password is incorrect.";
        }
        catch (Exception ex)
        {
            return $"Could not change password: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Adds a hardware or OS unlock method. Returns null on success or an error message.</summary>
    public async Task<string?> AddUnlockMethodAsync(ISecretProvider provider)
        => await SaveEventsAfterAsync(await AddUnlockMethodCoreAsync(provider));

    private async Task<string?> AddUnlockMethodCoreAsync(ISecretProvider provider)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        await _saveLock.WaitAsync();
        try
        {
            await _vault.AddSlotAsync(_sessionKey, provider, provider.DisplayName);
            SecurityLog.Record(SecurityEventsViewModel.MethodAdded, provider.DisplayName, save: false);
            await RefreshUnlockMethodsAsync();
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not add {provider.DisplayName}: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Saves the security event an operation recorded, after it let go of the save lock. Saving from inside it would start a
    /// second save that races the operation.
    /// </summary>
    private async Task<string?> SaveEventsAfterAsync(string? error)
    {
        if (error is null)
        {
            await PersistAsync();
        }

        return error;
    }

    /// <summary>Removes an unlock method. Returns null on success or an error message.</summary>
    public async Task<string?> RemoveUnlockMethodAsync(string slotId)
        => await SaveEventsAfterAsync(await RemoveUnlockMethodCoreAsync(slotId));

    private async Task<string?> RemoveUnlockMethodCoreAsync(string slotId)
    {
        if (_vault is null || _sessionKey is null)
        {
            return "Unlock the vault first.";
        }

        await _saveLock.WaitAsync();
        try
        {
            var slots = await _vault.ListSlotsAsync();
            var slot = slots.FirstOrDefault(s => s.Id == slotId);
            var provider = _providers.FirstOrDefault(p => p.SlotType == slot?.Type);
            await _vault.RemoveSlotAsync(_sessionKey, slotId, provider);
            SecurityLog.Record(SecurityEventsViewModel.MethodRemoved, slot?.Label ?? string.Empty, save: false);
            await RefreshUnlockMethodsAsync();
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not remove it: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Reloads the lists of unlock methods shown on the login screen and in Settings.</summary>
    public async Task RefreshUnlockMethodsAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            await RefreshUnlockMethodsCoreAsync();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshUnlockMethodsCoreAsync()
    {
        Login.UnlockMethods.Clear();
        Settings.UnlockMethods.Clear();
        Settings.AddableMethods.Clear();

        if (_vault is null || !await _vault.ExistsAsync())
        {
            return;
        }

        var slots = await _vault.ListSlotsAsync();
        foreach (var slot in slots)
        {
            Settings.UnlockMethods.Add(new UnlockMethodRowViewModel(slot));
        }

        var kdf = slots.FirstOrDefault(s => s.Type == SlotTypes.Password)?.Kdf;
        Settings.ProtectionSummary = kdf is null
            ? "Your vault uses an older format. Change the master password once to move it to the current one."
            : $"Your master password is stretched with Argon2id using {kdf.MemorySizeKib / 1024} MiB of memory and {kdf.Iterations} passes.";

        foreach (var provider in _providers)
        {
            if (!await provider.IsAvailableAsync())
            {
                continue;
            }

            var enrolled = slots.Count(s => s.Type == provider.SlotType);
            if (enrolled > 0)
            {
                Login.UnlockMethods.Add(new UnlockMethodViewModel(provider));
            }

            // A spare security key can be added, so losing one does not lose the quick unlock.
            if (enrolled == 0 || (provider.AllowsSeveral && enrolled < VaultService.MaxSlotsPerType))
            {
                Settings.AddableMethods.Add(new AddMethodOptionViewModel(provider));
            }
        }
    }

    [RelayCommand]
    private void Unlock()
    {
        IsUnlocked = true;
        NotifyActivity();
        CurrentPage = _showGuideNext ? Guide : Dashboard;
        _showGuideNext = false;
    }

    private IReadOnlyList<GuideStep> GuideSteps() =>
    [
        new(Loc.T("Set up a recovery key"),
            Loc.T("Nobody can reset a forgotten master password. A recovery key is the only way back in, so this step matters most."),
            Recovery.HasKey, Loc.T("Open Recovery"), ShowRecoveryCommand),
        new(Loc.T("Add a quicker way to unlock"),
            Loc.T("Windows Hello or a security key saves typing the master password every time. This is optional."),
            Settings.UnlockMethods.Count >= 2, Loc.T("Open Settings"), ShowSettingsCommand),
        new(Loc.T("Add your first account"),
            Loc.T("Type a secret key, or drop a QR code image on this window."),
            Dashboard.Accounts.Count > 0, Loc.T("Add account"), ShowAddAccountCommand),
        new(Loc.T("Bring in your passwords"),
            Loc.T("Import them from KeePass or KeePassXC, or add them one at a time on the Passwords page."),
            Passwords.Snapshot().Count > 0, Loc.T("Open Import"), ShowImportCommand),
    ];

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = Dashboard;

    [RelayCommand]
    private void ShowPasswords() => CurrentPage = Passwords;

    [RelayCommand]
    private void ShowHealth()
    {
        Health.Refresh();
        CurrentPage = Health;
    }

    [RelayCommand]
    private void ShowAddAccount() => CurrentPage = AccountEditor;

    [RelayCommand]
    private void ShowSettings() => CurrentPage = Settings;

    [RelayCommand]
    private void ShowBackup() => CurrentPage = Backup;

    [RelayCommand]
    private void ShowImport() => CurrentPage = Import;

    [RelayCommand]
    private void ShowHelp() => CurrentPage = Help;

    [RelayCommand]
    private void ShowVaults()
    {
        Vaults.Refresh();
        CurrentPage = Vaults;
    }

    [RelayCommand]
    private void ShowRecovery()
    {
        Recovery.Refresh();
        CurrentPage = Recovery;
    }

    [RelayCommand]
    private void ShowAbout() => AboutRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Locks the vault: wipes the session key and removes decrypted data from memory-backed view models.</summary>
    [RelayCommand]
    private void Lock()
    {
        if (_sessionKey is not null)
        {
            CryptographicOperations.ZeroMemory(_sessionKey);
            _sessionKey = null;
        }

        SecurityLog.Clear();

        // However the vault locks, a copied secret goes with it.
        _ = ClearClipboard?.Invoke();

        IsUnlocked = false;
        CurrentPage = Login;

        // The key is gone, so clearing these cannot trigger a save.
        Dashboard.Load([]);
        Passwords.Load([]);
        Passwords.ClearFormCommand.Execute(null);
        _syncDebounce?.Cancel();
        Sync.ClearSecrets();
        _olderCopyNoticed = false;
        AccountEditor.Secret = string.Empty;
        Import.ManualUri = string.Empty;
        Import.KeePass.Discard();
        Recovery.ClearSecrets();
        Recovery.CancelAutoBackup();
        Health.ClearSecrets();
        Dashboard.ResetNotices();
        LockWarningSeconds = null;
        Backup.MasterPassword = string.Empty;
        Backup.WebDavPassword = string.Empty;
        Settings.CurrentPassword = string.Empty;
        Settings.StrengthenPassword = string.Empty;
        Settings.NewPassword = string.Empty;
        Settings.ConfirmNewPassword = string.Empty;
        Login.MasterPassword = string.Empty;
        Login.ConfirmPassword = string.Empty;
        Login.Error = null;

        if (_vault is not null)
        {
            _ = RefreshUnlockMethodsAsync();
        }
    }

    // The active vault changed (another file opened, or a new vault started): lock and reload the login screen.
    private async Task OnActiveVaultChangedAsync()
    {
        Lock();
        Login.Forgot.Reset();
        Vaults.Refresh();
        Recovery.Refresh();
        await InitializeLoginAsync();
    }

    private async Task InitializeLoginAsync()
    {
        Login.IsCreateMode = !await _vault!.ExistsAsync();
        await RefreshUnlockMethodsAsync();
    }

    // This computer's key in the vault's sign-in record: a hash of a random install id, so the vault does not name the computer.
    private static string ComputerKey(ISettingsStore? store)
    {
        if (store is null)
        {
            return "demo";
        }

        try
        {
            var settings = store.Load();
            if (string.IsNullOrWhiteSpace(settings.InstallId))
            {
                settings.InstallId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                store.Save(settings);
            }

            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("sign-in|" + settings.InstallId)))[..32];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unsaved";
        }
    }

    private VaultContents BuildContents() => new()
    {
        Accounts = Dashboard.Accounts.Select(a => a.Account).ToList(),
        Passwords = Passwords.Snapshot(),
        Folders = Passwords.FolderSnapshot(),
        FolderIcons = Passwords.IconSnapshot(),
        RecoveryKeyFingerprint = Recovery.TrustedKeyFingerprint,
        Security = Settings.CurrentSecurity(),
        SaveCounter = _saveCounter,
        SignInFailuresSeen = _failuresSeen.TakeLast(50).ToDictionary(p => p.Key, p => p.Value),
        Tombstones = Passwords.TombstoneSnapshot().Concat(Dashboard.TombstoneSnapshot())
            .OrderByDescending(t => t.DeletedUtc).Take(Tombstone.MaxKept).ToList(),
        Sync = Sync.Settings,
        Events = SecurityLog.Snapshot(),
        AttachmentData = Passwords.AttachmentSnapshot(),
    };

    private async Task CompleteUnlockWithKeyAsync(byte[] key)
    {
        try
        {
            var contents = await _vault!.OpenWithKeyAsync(key);
            _sessionKey = key;
            var needsFirstSave = ApplyVaultSecurity(contents);
            await CheckSaveCounterAsync(contents);
            CompleteUnlock(contents);
            await RefreshUnlockMethodsAsync();
            if (needsFirstSave)
            {
                await PersistAsync();
            }

            if (Sync.Settings?.Automatic == true)
            {
                _ = RunAutomaticSyncAsync();
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    private void CompleteUnlock(VaultContents contents)
    {
        ReloadEntries(contents);
        SecurityLog.Load(contents.Events);
        _failuresSeen = contents.SignInFailuresSeen.Where(p => p.Value >= 0).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        Sync.Settings = contents.Sync;
        Recovery.TrustedKeyFingerprint = contents.RecoveryKeyFingerprint;
        Recovery.Refresh();
        Login.MasterPassword = string.Empty;
        Login.ConfirmPassword = string.Empty;
        Login.IsCreateMode = false;
        Login.Error = null;
        Unlock();
    }
}
