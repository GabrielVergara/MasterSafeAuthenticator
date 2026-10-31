// File:    PasswordsView.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Threading;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App.Views;

/// <summary>Passwords page. Copies secrets to the clipboard and clears them after 30 seconds.</summary>
public partial class PasswordsView : UserControl
{
    private static readonly TimeSpan ClipboardTimeout = TimeSpan.FromSeconds(30);
    private PasswordsViewModel? _vm;
    private DispatcherTimer? _clearTimer;

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

    private async void OnCopyRequested(object? sender, string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);

        _clearTimer?.Stop();
        _clearTimer = new DispatcherTimer { Interval = ClipboardTimeout };
        _clearTimer.Tick += async (_, _) =>
        {
            _clearTimer?.Stop();
            if (await clipboard.GetTextAsync() == text)
            {
                await clipboard.ClearAsync();
            }
        };
        _clearTimer.Start();
    }
}
