// File:    DashboardView.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App.Views;

public partial class DashboardView : UserControl
{
    private DashboardViewModel? _vm;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.CopyRequested -= OnCopyRequested;
            }

            _vm = DataContext as DashboardViewModel;
            if (_vm is not null)
            {
                _vm.CopyRequested += OnCopyRequested;
            }
        };
    }

    private async void OnCopyRequested(object? sender, string code)
    {
        if (_vm is not null && !await ClipboardHelper.CopyAsync(this, code))
        {
            _vm.StatusMessage = "Could not copy: the clipboard is in use by another program. Try again.";
        }
    }
}
