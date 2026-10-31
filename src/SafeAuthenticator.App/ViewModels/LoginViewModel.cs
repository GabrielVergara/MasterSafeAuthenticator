// File:    LoginViewModel.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// Login screen: collects the master password to unlock the vault.
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _masterPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ButtonText))]
    private bool _isCreateMode;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private bool _hasQuickUnlock;

    /// <summary>Raised when the user chooses to unlock with the saved Credential Manager key.</summary>
    public event EventHandler? QuickUnlockRequested;

    [RelayCommand]
    private void QuickUnlock()
    {
        Error = null;
        QuickUnlockRequested?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    /// <summary>Minimum master password length when creating a vault.</summary>
    public const int MinPasswordLength = 8;

    public string Title => IsCreateMode ? "Create your vault" : "Unlock your vault";

    public string ButtonText => IsCreateMode ? "Create vault" : "Unlock";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Raised with the entered password when the user requests unlock.</summary>
    public event EventHandler<string>? UnlockRequested;

    private bool CanUnlock => !IsBusy && MasterPassword.Length >= 1;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private void Unlock()
    {
        Error = null;
        if (IsCreateMode)
        {
            if (MasterPassword.Length < MinPasswordLength)
            {
                Error = $"Master password must be at least {MinPasswordLength} characters.";
                return;
            }

            if (MasterPassword != ConfirmPassword)
            {
                Error = "Passwords do not match.";
                return;
            }
        }

        UnlockRequested?.Invoke(this, MasterPassword);
    }
}
