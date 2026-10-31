// File:    SplashWindow.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MasterSafe.App.Views;

/// <summary>Borderless start-up window showing the product name and author.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
