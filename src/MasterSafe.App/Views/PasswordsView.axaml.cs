// File:    PasswordsView.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Threading;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App.Views;

/// <summary>Passwords page. Copies secrets to the clipboard and clears them after a while (see ClipboardPolicy).</summary>
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
                _vm.FolderNodes.CollectionChanged += (_, _) => ApplyFolderPaneWidth();
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PasswordsViewModel.UserFolderPaneWidth))
                    {
                        ApplyFolderPaneWidth();
                    }
                };
                ApplyFolderPaneWidth();
            }
        };

        SizeChanged += (_, _) => ApplyFolderPaneWidth();

        // Remember a width the user dragged to; a double-click goes back to fitting the folder names.
        FolderSplitter.DragCompleted += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.UserFolderPaneWidth = MainGrid.ColumnDefinitions[0].ActualWidth;
            }
        };
        FolderSplitter.DoubleTapped += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.UserFolderPaneWidth = null;
            }
        };
    }

    private void ApplyFolderPaneWidth()
    {
        if (_vm is null)
        {
            return;
        }

        // The list of passwords and the entry form need their room too, so the folder list never takes more than
        // what is left after them (the form is 340 wide and the list needs about 380).
        var room = Bounds.Width > 0 ? Bounds.Width - 340 - 380 - 30 : double.MaxValue;
        var width = Math.Min(_vm.EffectiveFolderPaneWidth, Math.Max(150, room));
        MainGrid.ColumnDefinitions[0].Width = new GridLength(width);
    }

    /// <summary>Closes the icon picker once an icon was chosen.</summary>
    private void OnIconPicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        // The button runs its command after this handler. Closing the picker right now would detach the button
        // (and its command) first, so the choice would be lost. Close it once the command has run.
        => Dispatcher.UIThread.Post(() => ChangeIconButton.Flyout?.Hide(), DispatcherPriority.Background);

    private async void OnCopyRequested(object? sender, string text)
    {
        if (_vm is not null && !await ClipboardHelper.CopyAsync(this, text))
        {
            _vm.Status = "Could not copy: the clipboard is in use by another program. Try again.";
        }
    }
}
