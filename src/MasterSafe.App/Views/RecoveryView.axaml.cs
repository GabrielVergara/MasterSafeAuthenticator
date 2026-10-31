// File:    RecoveryView.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;

namespace MasterSafe.App.Views;

public partial class RecoveryView : UserControl
{
    public RecoveryView() => InitializeComponent();

    // The copy is cleared from the clipboard after a while, like the other secrets the app copies.
    private async void OnCopyPassphrase(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.RecoveryViewModel { HasGeneratedPassphrase: true } vm)
        {
            var copied = await ClipboardHelper.CopyAsync(this, vm.GeneratedPassphrase);
            vm.Status = copied
                ? $"Copied the passphrase. The clipboard clears in {Services.ClipboardPolicy.ClearAfterText}."
                : "Could not copy: the clipboard is in use by another program. Try again.";
        }
    }
}
