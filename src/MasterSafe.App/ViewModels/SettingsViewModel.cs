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
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmNewPassword = string.Empty;

    [ObservableProperty]
    private string? _passwordChangeStatus;

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

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
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; the app keeps working with the values in memory.
        }
    }

    [RelayCommand]
    private void ApplyCurrentTheme() => App.ApplyTheme(SelectedTheme);

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
