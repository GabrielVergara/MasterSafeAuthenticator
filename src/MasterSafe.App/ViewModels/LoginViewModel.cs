// File:    LoginViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Core;
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

    /// <summary>Closes the program. Set by the application.</summary>
    public Action? ExitRequested { get; set; }

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke();

    /// <summary>Guidance shown while a new master password is typed.</summary>
    public string PasswordHint
    {
        get
        {
            if (MasterPassword.Length < MinPasswordLength)
            {
                return $"At least {MinPasswordLength} characters ({MinPasswordLength - MasterPassword.Length} more). A phrase of several words works well.";
            }

            var strength = PasswordStrength.Evaluate(MasterPassword);
            return strength.Score <= MinCreateScore - 1
                ? $"{strength.Label}. {strength.Hint}"
                : $"{strength.Label}. {(strength.Hint ?? "A phrase of several words is easier to remember than random characters.")}";
        }
    }

    /// <summary>The lowest strength score (0 to 4) accepted for a new master password: "Fair".</summary>
    public const int MinCreateScore = 2;

    /// <summary>Minimum master password length when creating a vault.</summary>
    public const int MinPasswordLength = 12;

    public string Title => IsCreateMode ? "Create your vault" : "Unlock your vault";

    public string ButtonText => IsCreateMode ? "Create vault" : "Unlock";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isBusy;

    /// <summary>True while sign-in with the master password is paused after too many wrong passwords.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isLockedOut;

    /// <summary>Tells how long the pause lasts, for example "Too many wrong passwords. Try again in 27 seconds."</summary>
    [ObservableProperty]
    private string? _lockoutMessage;

    /// <summary>Raised with the entered password when the user requests unlock.</summary>
    public event EventHandler<string>? UnlockRequested;

    private bool CanUnlock => !IsBusy && !IsLockedOut && MasterPassword.Length >= 1;

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

            if (PasswordStrength.Evaluate(MasterPassword).Score < MinCreateScore)
            {
                Error = "That master password is too easy to guess. Use a longer phrase of several words, or a longer mix of letters, digits and symbols.";
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
