// File:    SecretTextBox.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace MasterSafe.App.Views;

/// <summary>
/// A text box for passwords and other secrets. A normal text box gives its plain text to accessibility clients even when
/// it shows dots, so a screen reader could speak the master password and any program using UI Automation could read it.
/// This one reports dots instead, so a screen reader user still hears that something was typed, and how much.
/// </summary>
public class SecretTextBox : TextBox
{
    public SecretTextBox() => PasswordChar = '●';

    /// <summary>Looks exactly like a text box.</summary>
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new SecretPeer(this);

    private sealed class SecretPeer(SecretTextBox owner) : ControlAutomationPeer(owner), IValueProvider
    {
        public bool IsReadOnly => owner.IsReadOnly;

        public string Value => new('●', owner.Text?.Length ?? 0);

        // Voice input and other assistive tools may still type into the box.
        public void SetValue(string? value) => owner.Text = value;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    }
}
