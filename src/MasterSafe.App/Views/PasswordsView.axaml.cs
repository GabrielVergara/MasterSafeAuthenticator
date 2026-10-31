// File:    PasswordsView.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App.Views;

/// <summary>Passwords page. Copies secrets to the clipboard and clears them after 30 seconds.</summary>
public partial class PasswordsView : UserControl
{
    private PasswordsViewModel? _vm;

    public PasswordsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.CopyRequested -= OnCopyRequested;
            }

            _vm = DataContext as PasswordsViewModel;
            if (_vm is not null)
            {
                _vm.CopyRequested += OnCopyRequested;
            }
        };
    }

    /// <summary>Closes the icon picker once an icon was chosen.</summary>
    private void OnIconPicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ChangeIconButton.Flyout?.Hide();

    private async void OnCopyRequested(object? sender, string text)
    {
        if (_vm is not null && !await ClipboardHelper.CopyAsync(this, text))
        {
            _vm.Status = "Could not copy: the clipboard is in use by another program. Try again.";
        }
    }
}
