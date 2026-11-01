// File:    SettingsViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Settings: theme selection, vault preferences and master password change.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private AppTheme _selectedTheme = AppTheme.System;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private int _autoLockMinutes = 5;

    [ObservableProperty]
    private bool _lockOnSessionLock = true;

    [ObservableProperty]
    private bool _lockOnMinimize;

    [ObservableProperty]
    private bool _blockScreenCapture = true;

    [ObservableProperty]
    private bool _showReminders = true;

    /// <summary>The sizes offered for the text and the whole window, in percent.</summary>
    public static IReadOnlyList<int> TextScaleOptions { get; } = [100, 125, 150, 175, 200];

    /// <summary>How large the text and the window are shown, in percent (100 to 200).</summary>
    [ObservableProperty]
    private int _textScalePercent = 100;

    [ObservableProperty]
    private bool _breachCheckEnabled;

    [ObservableProperty]
    private bool _checkForUpdatesOnStart;

    /// <summary>The "check for updates" section. Set by the shell.</summary>
    [ObservableProperty]
    private UpdatesViewModel? _updates;

    [ObservableProperty]
    private ClipboardDelayOption _selectedClipboardDelay = ClipboardPolicy.Current;

    /// <summary>The waits offered for clearing the clipboard.</summary>
    public IReadOnlyList<ClipboardDelayOption> ClipboardDelays => ClipboardPolicy.Options;

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmNewPassword = string.Empty;

    [ObservableProperty]
    private string? _passwordChangeStatus;

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark, AppTheme.HighContrast];

    /// <summary>
    /// Performs the change (current, new) and returns null on success or an error message.
    /// Supplied by the main view model; null in design/test mode.
    /// </summary>
    public Func<string, string, Task<string?>>? ChangePasswordHandler { get; set; }

    /// <summary>Unlock methods currently set up for the vault, including the master password.</summary>
    public ObservableCollection<UnlockMethodRowViewModel> UnlockMethods { get; } = [];

    /// <summary>Hardware or OS methods that can be added on this computer.</summary>
    public ObservableCollection<AddMethodOptionViewModel> AddableMethods { get; } = [];

    /// <summary>Status line for the unlock methods section.</summary>
    [ObservableProperty]
    private string? _unlockMethodStatus;

    /// <summary>Adds an unlock method; returns null on success or an error message.</summary>
    public Func<ISecretProvider, Task<string?>>? AddMethodHandler { get; set; }

    /// <summary>Removes an unlock method by slot id; returns null on success or an error message.</summary>
    public Func<string, Task<string?>>? RemoveMethodHandler { get; set; }

    [RelayCommand]
    private async Task AddMethodAsync(AddMethodOptionViewModel option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (AddMethodHandler is null)
        {
            UnlockMethodStatus = "No vault is open.";
            return;
        }

        UnlockMethodStatus = $"Waiting for {option.Provider.DisplayName}... {option.Provider.EnrollmentHint}".TrimEnd();
        var error = await AddMethodHandler(option.Provider);
        UnlockMethodStatus = error ?? $"{option.Provider.DisplayName} added.";
    }

    [RelayCommand]
    private async Task RemoveMethodAsync(UnlockMethodRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (RemoveMethodHandler is null)
        {
            UnlockMethodStatus = "No vault is open.";
            return;
        }

        var error = await RemoveMethodHandler(row.SlotId);
        UnlockMethodStatus = error ?? $"{row.Label} removed.";
    }

    /// <summary>Creates the settings page. With a store, values are loaded from it and saved when they change.</summary>
    public SettingsViewModel(ISettingsStore? store = null)
    {
        _store = store;
        if (store is not null)
        {
            var saved = store.Load();
            _selectedTheme = saved.Theme;
            _minimizeToTray = saved.MinimizeToTray;
            _autoLockMinutes = saved.AutoLockMinutes;
            _lockOnSessionLock = saved.LockOnSessionLock;
            _lockOnMinimize = saved.LockOnMinimize;
            _globalLockHotkey = saved.GlobalLockHotkey;
            _easyReading = saved.EasyReading;
            _blockScreenCapture = saved.BlockScreenCapture;
            _breachCheckEnabled = saved.BreachCheckEnabled;
            _showReminders = saved.ShowReminders;
            _textScalePercent = NearestTextScale(saved.TextScalePercent);
            _language = Loc.Choices.Contains(saved.Language) ? saved.Language : "System";
            _checkForUpdatesOnStart = saved.CheckForUpdatesOnStart;
            ClipboardPolicy.Use(saved.ClipboardClearSeconds);
            _selectedClipboardDelay = ClipboardPolicy.Current;
        }
    }

    private readonly ISettingsStore? _store;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        App.ApplyTheme(value);
        SaveSettings();
    }

    partial void OnMinimizeToTrayChanged(bool value) => SaveSettings();

    partial void OnAutoLockMinutesChanged(int value) => SaveSettings();

    partial void OnLockOnSessionLockChanged(bool value) => SaveSettings();

    partial void OnLockOnMinimizeChanged(bool value) => SaveSettings();

    /// <summary>Ctrl+Alt+L locks the vault from any program (Windows).</summary>
    [ObservableProperty]
    private bool _globalLockHotkey;

    /// <summary>Shown when the shortcut could not be turned on.</summary>
    [ObservableProperty]
    private string? _lockHotkeyNote;

    partial void OnGlobalLockHotkeyChanged(bool value) => SaveSettings();

    /// <summary>More space between letters, words and lines.</summary>
    [ObservableProperty]
    private bool _easyReading;

    partial void OnEasyReadingChanged(bool value) => SaveSettings();

    partial void OnBlockScreenCaptureChanged(bool value) => SaveSettings();

    partial void OnBreachCheckEnabledChanged(bool value) => SaveSettings();

    partial void OnShowRemindersChanged(bool value) => SaveSettings();

    partial void OnTextScalePercentChanged(int value) => SaveSettings();

    /// <summary>"System", "en", "es" or "fi". Takes full effect the next time the program starts.</summary>
    [ObservableProperty]
    private string _language = "System";

    public IReadOnlyList<string> Languages => Loc.Choices;

    partial void OnLanguageChanged(string value)
    {
        SaveSettings();
        LanguageNote = Loc.T("The language changes completely the next time MasterSafe starts.");
    }

    [ObservableProperty]
    private string? _languageNote;

    /// <summary>The offered size closest to <paramref name="percent"/>; 100 when it is 0 or missing.</summary>
    public static int NearestTextScale(int percent)
        => percent <= 0 ? 100 : TextScaleOptions.OrderBy(o => Math.Abs(o - percent)).First();

    partial void OnCheckForUpdatesOnStartChanged(bool value) => SaveSettings();

    partial void OnSelectedClipboardDelayChanged(ClipboardDelayOption value)
    {
        ClipboardPolicy.Use(value.Seconds);
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            // Load first so settings owned by other screens (recovery key, vault list) are kept.
            var settings = _store.Load();
            settings.Theme = SelectedTheme;
            settings.MinimizeToTray = MinimizeToTray;
            settings.AutoLockMinutes = AutoLockMinutes;
            settings.ClipboardClearSeconds = SelectedClipboardDelay.Seconds;
            settings.LockOnSessionLock = LockOnSessionLock;
            settings.LockOnMinimize = LockOnMinimize;
            settings.GlobalLockHotkey = GlobalLockHotkey;
            settings.EasyReading = EasyReading;
            settings.BlockScreenCapture = BlockScreenCapture;
            settings.BreachCheckEnabled = BreachCheckEnabled;
            settings.ShowReminders = ShowReminders;
            settings.TextScalePercent = TextScalePercent;
            settings.Language = Language;
            settings.CheckForUpdatesOnStart = CheckForUpdatesOnStart;
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; the app keeps working with the values in memory.
        }
    }

    [RelayCommand]
    private void ApplyCurrentTheme() => App.ApplyTheme(SelectedTheme);

    /// <summary>Describes the key-derivation settings of the master password slot, for example "128 MiB, 3 passes".</summary>
    [ObservableProperty]
    private string _protectionSummary = string.Empty;

    /// <summary>The master password, typed to move the vault to stronger key-derivation settings.</summary>
    [ObservableProperty]
    private string _strengthenPassword = string.Empty;

    [ObservableProperty]
    private string? _strengthenStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StrengthenCommand))]
    private bool _isStrengthening;

    /// <summary>Re-encrypts the vault under settings tuned to this computer; given the master password, returns an error or null.</summary>
    public Func<string, Task<string>>? StrengthenHandler { get; set; }

    private bool CanStrengthen => !IsStrengthening && StrengthenHandler is not null;

    [RelayCommand(CanExecute = nameof(CanStrengthen))]
    private async Task StrengthenAsync()
    {
        if (StrengthenHandler is null || StrengthenPassword.Length == 0)
        {
            StrengthenStatus = "Type your master password first.";
            return;
        }

        IsStrengthening = true;
        StrengthenStatus = "Testing how strong the settings can be on this computer. This takes a few seconds...";
        try
        {
            StrengthenStatus = await StrengthenHandler(StrengthenPassword);
            StrengthenPassword = string.Empty;
        }
        finally
        {
            IsStrengthening = false;
        }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (ChangePasswordHandler is null)
        {
            PasswordChangeStatus = "No vault is open.";
            return;
        }

        if (NewPassword.Length < LoginViewModel.MinPasswordLength)
        {
            PasswordChangeStatus = $"New password must be at least {LoginViewModel.MinPasswordLength} characters.";
            return;
        }

        if (NewPassword != ConfirmNewPassword)
        {
            PasswordChangeStatus = "New passwords do not match.";
            return;
        }

        var error = await ChangePasswordHandler(CurrentPassword, NewPassword);
        if (error is null)
        {
            CurrentPassword = NewPassword = ConfirmNewPassword = string.Empty;
            PasswordChangeStatus = "Master password changed.";
        }
        else
        {
            PasswordChangeStatus = error;
        }
    }
}

/// <summary>One unlock method listed in Settings.</summary>
public sealed class UnlockMethodRowViewModel(KeySlotInfo slot)
{
    /// <summary>Slot identifier.</summary>
    public string SlotId { get; } = slot.Id;

    /// <summary>Name shown to the user.</summary>
    public string Label { get; } = slot.Label;

    /// <summary>When it was added (local date).</summary>
    public string Added { get; } = DateTimeOffset.TryParse(slot.CreatedUtc, out var d)
        ? $"Added {d.LocalDateTime:yyyy-MM-dd}"
        : string.Empty;

    /// <summary>The master password is permanent; other methods can be removed.</summary>
    public bool CanRemove { get; } = slot.Type != SlotTypes.Password;
}

/// <summary>An "add" button in Settings for one available unlock method.</summary>
public sealed class AddMethodOptionViewModel(ISecretProvider provider)
{
    /// <summary>The provider to enroll.</summary>
    public ISecretProvider Provider { get; } = provider;

    /// <summary>Button text.</summary>
    public string Text => $"Add {Provider.DisplayName}";
}
