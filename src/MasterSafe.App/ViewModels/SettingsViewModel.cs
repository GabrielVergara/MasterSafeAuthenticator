// File:    SettingsViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    partial void OnSelectedThemeChanged(AppTheme value) => App.ApplyTheme(value);

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
