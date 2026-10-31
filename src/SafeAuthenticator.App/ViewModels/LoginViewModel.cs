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
        UnlockRequested?.Invoke(this, MasterPassword);
    }
}
