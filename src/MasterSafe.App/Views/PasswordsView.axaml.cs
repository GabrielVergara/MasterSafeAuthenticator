// File:    PasswordsView.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;

namespace MasterSafe.App.Views;

/// <summary>Passwords page. Copies secrets to the clipboard and clears them after a while (see ClipboardPolicy).</summary>
public partial class PasswordsView : UserControl
{
    /// <summary>The clipboard-style format that carries the id of an entry while it is dragged onto a folder.</summary>
    internal static readonly DataFormat<string> EntryDragFormat = DataFormat.CreateStringApplicationFormat("mastersafe-entry-id");

    private PasswordsViewModel? _vm;
    private PointerPressedEventArgs? _dragStart;
    private PasswordRow? _dragRow;
    private Point _dragOrigin;

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
                _vm.FocusSearchRequested += (_, _) => SearchBox.Focus();
                _vm.FocusTitleRequested += (_, _) => TitleBox.Focus();
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

        // Shortcuts only reach this page when the keyboard focus is inside it, so a click anywhere on the page that
        // does not land on a control that takes focus itself (a card, an empty area) focuses the page.
        Focusable = true;
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.Handled && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
            {
                Focus();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Background);

        // Entries can be dragged onto a folder in the list.
        DragDrop.SetAllowDrop(FolderList, true);
        FolderList.AddHandler(DragDrop.DragOverEvent, OnFolderDragOver);
        FolderList.AddHandler(DragDrop.DropEvent, OnFolderDrop);

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

    // ------------------------------------------------------------------ keyboard shortcuts

    /// <summary>
    /// Ctrl+F search, Ctrl+N new entry, Ctrl+B copy the user name, Ctrl+C copy the password, Ctrl+U open the site, Delete
    /// delete. They act on the current entry (the one last clicked). Text boxes keep their own Ctrl+C and Delete.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null || e.Handled || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
        {
            if (_vm is not null && !e.Handled && e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
            {
                _vm.FocusSearchCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        var control = e.KeyModifiers == KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.F when control:
                _vm.FocusSearchCommand.Execute(null);
                break;
            case Key.N when control:
                _vm.NewEntryCommand.Execute(null);
                break;
            case Key.B when control:
                _vm.CopyCurrentUserCommand.Execute(null);
                break;
            case Key.C when control:
                _vm.CopyCurrentPasswordCommand.Execute(null);
                break;
            case Key.U when control:
                _vm.OpenCurrentUrlCommand.Execute(null);
                break;
            case Key.Delete when e.KeyModifiers == KeyModifiers.None:
                _vm.DeleteCurrentCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ------------------------------------------------------------------ cards

    private static PasswordRow? RowOf(object? sender) => (sender as Control)?.DataContext as PasswordRow;

    /// <summary>True when the event came from a button, check box or text box inside a card, which handle their own clicks.</summary>
    private static bool FromInnerControl(RoutedEventArgs e)
        => e.Source is Visual v && (v.FindAncestorOfType<Button>(true) is not null
                                    || v.FindAncestorOfType<CheckBox>(true) is not null
                                    || v.FindAncestorOfType<TextBox>(true) is not null);

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _vm?.SetCurrentCommand.Execute(row.Entry);
        }
    }

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Double-clicking a card copies its password, like in other password managers.
        if (RowOf(sender) is { IsActive: true } row && !FromInnerControl(e))
        {
            _vm?.SetCurrentCommand.Execute(row.Entry);
            _vm?.CopyPasswordCommand.Execute(row.Entry);
        }
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RowOf(sender) is { IsActive: true } row && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !FromInnerControl(e))
        {
            _dragStart = e;
            _dragRow = row;
            _dragOrigin = e.GetPosition(this);
        }
    }

    private async void OnCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || _dragRow is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var moved = e.GetPosition(this) - _dragOrigin;
        if (Math.Abs(moved.X) + Math.Abs(moved.Y) < 10)
        {
            return;
        }

        var start = _dragStart;
        var id = _dragRow.Entry.Id;
        _dragStart = null;
        _dragRow = null;

        var item = DataTransferItem.Create(EntryDragFormat, id.ToString());
        var data = new DataTransfer();
        data.Add(item);
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
    }

    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        _dragRow = null;
    }

    // ------------------------------------------------------------------ right-click menu of a card

    private void OnMenuCopyPassword(object? sender, RoutedEventArgs e) => _vm?.CopyPasswordCommand.Execute(RowOf(sender)?.Entry);

    private void OnMenuCopyUser(object? sender, RoutedEventArgs e) => _vm?.CopyUserNameCommand.Execute(RowOf(sender)?.Entry);

    private void OnMenuOpenSite(object? sender, RoutedEventArgs e) => _vm?.OpenUrlCommand.Execute(RowOf(sender)?.Entry);

    private void OnMenuEdit(object? sender, RoutedEventArgs e) => _vm?.EditCommand.Execute(RowOf(sender)?.Entry);

    private void OnMenuRestore(object? sender, RoutedEventArgs e) => _vm?.RestoreCommand.Execute(RowOf(sender)?.Entry);

    private void OnMenuDelete(object? sender, RoutedEventArgs e) => _vm?.RequestDeleteCommand.Execute(RowOf(sender)?.Entry);

    // ------------------------------------------------------------------ folders: right-click menu and drops

    private static FolderNode? NodeOf(object? sender) => (sender as Control)?.DataContext as FolderNode;

    private void SelectNode(FolderNode? node)
    {
        if (_vm is not null && node is not null)
        {
            _vm.SelectedNode = node;
        }
    }

    private void OnFolderMenuNew(object? sender, RoutedEventArgs e)
    {
        SelectNode(NodeOf(sender));
        _vm!.Status = "Type a name in the box under the folder list, then choose New folder.";
        FolderNameBox.Focus();
    }

    private void OnFolderMenuRename(object? sender, RoutedEventArgs e)
    {
        var node = NodeOf(sender);
        if (node?.Path is null)
        {
            _vm!.Status = "Choose a folder to rename. \"All passwords\" and the recycle bin keep their names.";
            return;
        }

        SelectNode(node);
        _vm!.FolderName = FolderPath.Name(node.Path);
        _vm.Status = "Change the name in the box under the folder list, then choose Rename selected.";
        FolderNameBox.Focus();
    }

    private void OnFolderMenuIcon(object? sender, RoutedEventArgs e)
    {
        var node = NodeOf(sender);
        if (node?.Path is null)
        {
            return;
        }

        SelectNode(node);
        Dispatcher.UIThread.Post(() => ChangeIconButton.Flyout?.ShowAt(ChangeIconButton), DispatcherPriority.Background);
    }

    private void OnFolderMenuDelete(object? sender, RoutedEventArgs e)
    {
        var node = NodeOf(sender);
        if (node?.Path is null)
        {
            return;
        }

        SelectNode(node);
        _vm!.DeleteFolderCommand.Execute(null);
    }

    private FolderNode? NodeAt(DragEventArgs e)
        => (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true)?.DataContext as FolderNode;

    private void OnFolderDragOver(object? sender, DragEventArgs e)
    {
        var node = NodeAt(e);
        e.DragEffects = e.DataTransfer.Contains(EntryDragFormat) && node is { } n && (n.IsBin || n.Path is not null)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFolderDrop(object? sender, DragEventArgs e)
    {
        if (_vm is not null
            && e.DataTransfer.TryGetValue(EntryDragFormat) is { } text
            && Guid.TryParse(text, out var id))
        {
            _vm.MoveDraggedTo(id, NodeAt(e));
        }

        e.Handled = true;
    }

    private async void OnCopyRequested(object? sender, string text)
    {
        if (_vm is not null && !await ClipboardHelper.CopyAsync(this, text))
        {
            _vm.Status = "Could not copy: the clipboard is in use by another program. Try again.";
        }
    }
}
