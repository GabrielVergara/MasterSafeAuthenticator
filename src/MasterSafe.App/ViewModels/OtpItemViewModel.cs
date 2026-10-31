// File:    OtpItemViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using CommunityToolkit.Mvvm.ComponentModel;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// A single account tile on the dashboard: issuer, live OTP code and progress to next refresh.
/// </summary>
public sealed partial class OtpItemViewModel : ViewModelBase
{
    private readonly OtpAccount _account;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCode))]
    private string _code = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private int _secondsRemaining;

    public OtpItemViewModel(OtpAccount account)
    {
        _account = account ?? throw new ArgumentNullException(nameof(account));
    }

    public Guid Id => _account.Id;

    /// <summary>The underlying account model.</summary>
    public OtpAccount Account => _account;

    public string Issuer => string.IsNullOrEmpty(_account.Issuer) ? "(no issuer)" : _account.Issuer;

    public string Label => _account.Label;

    public int Period => _account.Period;

    /// <summary>Recomputes the code and progress for the supplied time.</summary>
    public void Update(DateTimeOffset now)
    {
        Code = _account.GenerateCode(now);

        if (_account.Type == OtpType.Totp)
        {
            SecondsRemaining = TotpGenerator.RemainingSeconds(now, _account.Period);
            Progress = (double)SecondsRemaining / _account.Period;
        }
        else
        {
            SecondsRemaining = 0;
            Progress = 1.0;
        }
    }

    /// <summary>The formatted code grouped for readability, e.g. "123 456".</summary>
    public string DisplayCode => Code.Length == 6 ? $"{Code[..3]} {Code[3..]}" : Code;
}
