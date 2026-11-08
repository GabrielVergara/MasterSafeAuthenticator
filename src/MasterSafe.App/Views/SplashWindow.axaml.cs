// File:    SplashWindow.axaml.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MasterSafe.App.Services;

namespace MasterSafe.App.Views;

/// <summary>Borderless start-up window showing the product name and author, with the logo's intro played once.</summary>
public partial class SplashWindow : Window
{
    /// <summary>How long the logo takes to draw itself. It ends before the splash closes, on the finished logo.</summary>
    public static readonly TimeSpan IntroDuration = TimeSpan.FromMilliseconds(1600);

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public SplashWindow()
    {
        InitializeComponent();
        Logo = this.FindControl<LogoMark>("IntroLogo")!;

        // With animations turned off in the system, the finished logo shows at once.
        if (MotionPreference.IsReduced())
        {
            Logo.Progress = 1;
            return;
        }

        Logo.Progress = 0;
        _timer.Tick += (_, _) => Advance();
        Opened += (_, _) =>
        {
            _clock.Start();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>The animated logo.</summary>
    public LogoMark Logo { get; }

    private void Advance()
    {
        var progress = _clock.Elapsed / IntroDuration;
        Logo.Progress = progress;
        if (progress >= 1)
        {
            _timer.Stop();
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
