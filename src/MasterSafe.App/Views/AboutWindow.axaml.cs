// File:    AboutWindow.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App.Views;

public partial class AboutWindow : Window
{
    private const double BaseWidth = 560;

    public AboutWindow()
        : this(1.0, null)
    {
    }

    /// <summary>
    /// Opens at the text size of the main window. The height follows the content, so nothing is cut off, and stops at the
    /// screen: only then does the content scroll.
    /// </summary>
    public AboutWindow(double scale, Screen? screen)
    {
        InitializeComponent();
        DataContext = new AboutViewModel();

        scale = Math.Clamp(scale, 0.5, 4.0);
        this.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform = new ScaleTransform(scale, scale);

        var maxWidth = double.PositiveInfinity;
        if (screen is not null)
        {
            maxWidth = screen.WorkingArea.Width / screen.Scaling * 0.95;
            MaxHeight = screen.WorkingArea.Height / screen.Scaling * 0.9;
        }

        Width = Math.Min(maxWidth, BaseWidth * scale);
        MinWidth = Math.Min(Width, MinWidth * scale);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
