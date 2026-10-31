// File:    MainWindow.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Any input restarts the auto-lock countdown, even when a control handles it.
        AddHandler(PointerMovedEvent, OnActivity, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnActivity, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnActivity, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // QR code images can be dropped anywhere on the window while the vault is unlocked.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var canDrop = DataContext is MainWindowViewModel { IsUnlocked: true } && e.DataTransfer.TryGetFiles() is { Length: > 0 };
        e.DragEffects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var paths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        vm.ImportDroppedFiles(paths);
        e.Handled = true;
    }

    private void OnActivity(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.NotifyActivity();

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.AboutRequested -= OnAboutRequested;
            vm.AboutRequested += OnAboutRequested;
        }
    }

    private void OnAboutRequested(object? sender, System.EventArgs e) => ShowAbout();

    /// <summary>Opens the About dialog, owned by this window.</summary>
    public void ShowAbout()
    {
        var about = new AboutWindow();
        about.ShowDialog(this);
    }
}
