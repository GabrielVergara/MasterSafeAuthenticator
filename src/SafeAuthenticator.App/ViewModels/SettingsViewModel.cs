// File:    SettingsViewModel.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// Settings: theme selection and vault preferences.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private AppTheme _selectedTheme = AppTheme.System;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private int _autoLockMinutes = 5;

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    partial void OnSelectedThemeChanged(AppTheme value) => App.ApplyTheme(value);

    [RelayCommand]
    private void ApplyCurrentTheme() => App.ApplyTheme(SelectedTheme);
}
