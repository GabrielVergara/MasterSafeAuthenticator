// File:    AccountEditorViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Editor for creating or modifying an account.
/// </summary>
public sealed partial class AccountEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _issuer = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _secret = string.Empty;

    [ObservableProperty]
    private OtpAlgorithm _algorithm = OtpAlgorithm.Sha1;

    [ObservableProperty]
    private int _digits = 6;

    [ObservableProperty]
    private int _period = 30;

    [ObservableProperty]
    private string? _validationError;

    public IReadOnlyList<OtpAlgorithm> Algorithms { get; } =
        [OtpAlgorithm.Sha1, OtpAlgorithm.Sha256, OtpAlgorithm.Sha512];

    public IReadOnlyList<int> DigitOptions { get; } = [6, 8];

    public IReadOnlyList<int> PeriodOptions { get; } = [30, 60];

    /// <summary>Raised with the validated account when the user saves.</summary>
    public event EventHandler<OtpAccount>? AccountSaved;

    [RelayCommand]
    private void Save()
    {
        if (!TryBuild(out var account) || account is null)
        {
            return;
        }

        AccountSaved?.Invoke(this, account);
        Issuer = string.Empty;
        Label = string.Empty;
        Secret = string.Empty;
    }

    /// <summary>Populates the editor from an existing account.</summary>
    public void LoadFrom(OtpAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        Issuer = account.Issuer;
        Label = account.Label;
        Secret = account.Secret;
        Algorithm = account.Algorithm;
        Digits = account.Digits;
        Period = account.Period;
    }

    /// <summary>Attempts to build a validated account from the current form values.</summary>
    public bool TryBuild(out OtpAccount? account)
    {
        var candidate = new OtpAccount
        {
            Issuer = Issuer.Trim(),
            Label = Label.Trim(),
            Secret = Secret.Trim(),
            Algorithm = Algorithm,
            Digits = Digits,
            Period = Period,
        };

        if (!candidate.IsValid(out var error))
        {
            ValidationError = error;
            account = null;
            return false;
        }

        ValidationError = null;
        account = candidate;
        return true;
    }
}
