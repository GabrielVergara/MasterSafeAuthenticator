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

        // When the text size is turned up, the window grows with it (as far as the screen allows) so the same amount of
        // content still fits. Making it smaller again never shrinks a window the user enlarged.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel model)
            {
                model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.TextScale))
                    {
                        GrowForScale(model.TextScale);
                    }
                };
            }
        };

        // QR code images can be dropped anywhere on the window while the vault is unlocked.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void GrowForScale(double scale)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        var area = Screens.ScreenFromWindow(this)?.WorkingArea;
        var maxWidth = area is { } a ? a.Width / (Screens.ScreenFromWindow(this)?.Scaling ?? 1.0) : double.PositiveInfinity;
        var maxHeight = area is { } b ? b.Height / (Screens.ScreenFromWindow(this)?.Scaling ?? 1.0) : double.PositiveInfinity;
        Width = Math.Min(maxWidth, Math.Max(Width, 1000 * scale));
        Height = Math.Min(maxHeight, Math.Max(Height, 680 * scale));
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
