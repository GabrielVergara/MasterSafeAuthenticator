// File:    AutoHideReveal.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using Avalonia.Threading;

namespace MasterSafe.App.Services;

/// <summary>
/// Hides a password that was revealed with the eye button after a short time, so it is not left on screen for
/// someone walking past or for a screen recording.
/// </summary>
public static class AutoHideReveal
{
    public static TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(20);

    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        TextBox.RevealPasswordProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<bool>>(e =>
        {
            if (e.Sender is not TextBox box || e.NewValue.GetValueOrDefault() is not true)
            {
                return;
            }

            var timer = new DispatcherTimer { Interval = Delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                box.RevealPassword = false;
            };
            timer.Start();
            box.DetachedFromVisualTree += (_, _) =>
            {
                timer.Stop();
                box.RevealPassword = false;
            };
        }));
    }
}
