// File:    LoginViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Login screen: collects the master password to unlock the vault.
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    [NotifyPropertyChangedFor(nameof(PasswordHint))]
    private string _masterPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ButtonText))]
    private bool _isCreateMode;

    /// <summary>Hardware or OS unlock methods (Windows Hello, security key) set up for this vault.</summary>
    public ObservableCollection<UnlockMethodViewModel> UnlockMethods { get; } = [];

    /// <summary>Raised when the user chooses an unlock method other than the master password.</summary>
    public event EventHandler<ISecretProvider>? MethodUnlockRequested;

    [RelayCommand]
    private void UnlockWithMethod(UnlockMethodViewModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        Error = null;
        MethodUnlockRequested?.Invoke(this, method.Provider);
    }

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    /// <summary>The "forgot your master password" panel. Set by the shell.</summary>
    public ForgotPasswordViewModel Forgot { get; set; } = new();

    /// <summary>Opens the "forgot your master password" panel.</summary>
    [RelayCommand]
    private void ShowForgot() => Forgot.IsOpen = true;

    /// <summary>Guidance shown while a new master password is typed.</summary>
    public string PasswordHint => MasterPassword.Length >= MinPasswordLength
        ? "Length is good. A phrase of several words is easier to remember than random characters."
        : $"At least {MinPasswordLength} characters ({MinPasswordLength - MasterPassword.Length} more). A phrase of several words works well.";

    /// <summary>Minimum master password length when creating a vault.</summary>
    public const int MinPasswordLength = 12;

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

/// <summary>A button on the login screen for one hardware or OS unlock method.</summary>
public sealed class UnlockMethodViewModel(ISecretProvider provider)
{
    /// <summary>The provider that performs the unlock.</summary>
    public ISecretProvider Provider { get; } = provider;

    /// <summary>Button text.</summary>
    public string Text => $"Unlock with {Provider.DisplayName}";
}
