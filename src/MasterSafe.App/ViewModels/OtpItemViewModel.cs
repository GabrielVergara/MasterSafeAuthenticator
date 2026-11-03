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

    /// <summary>How long a code stays shown after it was clicked.</summary>
    public static readonly TimeSpan RevealTime = TimeSpan.FromSeconds(20);

    private DateTimeOffset _now;
    private DateTimeOffset _hideAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCode))]
    private string _code = string.Empty;

    /// <summary>True while the code is covered until it is clicked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCode), nameof(IsCovered), nameof(RevealText))]
    private bool _isHidden;

    /// <summary>True when the "show code" button is offered.</summary>
    [ObservableProperty]
    private bool _canHide;

    /// <summary>True when the code is shown as dots.</summary>
    public bool IsCovered => IsHidden;

    /// <summary>The text of the button that shows or hides the code.</summary>
    public string RevealText => IsHidden ? "Show code" : "Hide code";

    /// <summary>Shows a hidden code for a short time, or hides it again.</summary>
    public void ToggleReveal()
    {
        IsHidden = !IsHidden;
        _hideAt = _now + RevealTime;
    }

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

    /// <summary>The group of the account; empty for none.</summary>
    public string Group => _account.Group;

    /// <summary>True when the account is in a group.</summary>
    public bool HasGroup => _account.Group.Length > 0;

    public int Period => _account.Period;

    /// <summary>Recomputes the code and progress for the supplied time.</summary>
    public void Update(DateTimeOffset now)
    {
        _now = now;
        if (CanHide && !IsHidden && now >= _hideAt)
        {
            IsHidden = true;
        }

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
    public string DisplayCode => IsHidden
        ? (Code.Length == 6 ? "••• •••" : new string('•', Code.Length))
        : Code.Length == 6 ? $"{Code[..3]} {Code[3..]}" : Code;
}
