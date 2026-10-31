// File:    PasswordsViewModel.Strength.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using CommunityToolkit.Mvvm.ComponentModel;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>The strength meter under the password field of the entry form.</summary>
public sealed partial class PasswordsViewModel
{
    /// <summary>0 (very weak) to 4 (strong) for the password in the form.</summary>
    [ObservableProperty]
    private int _strengthScore;

    /// <summary>For example "Good, about 62 bits. A few more characters would make it strong."</summary>
    [ObservableProperty]
    private string _strengthText = string.Empty;

    partial void OnPasswordChanged(string value) => UpdateStrength();

    partial void OnTitleChanged(string value) => UpdateStrength();

    partial void OnUserNameChanged(string value) => UpdateStrength();

    private void UpdateStrength()
    {
        if (string.IsNullOrEmpty(Password))
        {
            StrengthScore = 0;
            StrengthText = string.Empty;
            return;
        }

        var result = PasswordStrength.Evaluate(Password, Title, UserName);
        StrengthScore = result.Score;
        StrengthText = result.Hint is null
            ? $"{result.Label} (about {result.Bits:0} bits)"
            : $"{result.Label} (about {result.Bits:0} bits). {result.Hint}";
    }
}
