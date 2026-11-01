// File:    AutoHideReveal.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.CompilerServices;
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

    private static readonly ConditionalWeakTable<TextBox, DispatcherTimer> Timers = [];
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
            if (e.Sender is not TextBox box)
            {
                return;
            }

            // Hiding by hand, or revealing again, starts over: an older timer must not hide a later reveal early.
            if (Timers.TryGetValue(box, out var running))
            {
                running.Stop();
                Timers.Remove(box);
            }

            if (e.NewValue.GetValueOrDefault() is not true)
            {
                return;
            }

            var timer = new DispatcherTimer { Interval = Delay };
            timer.Tick += (_, _) => box.RevealPassword = false;
            Timers.Add(box, timer);
            timer.Start();

            void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
            {
                box.DetachedFromVisualTree -= OnDetached;
                box.RevealPassword = false;
            }

            box.DetachedFromVisualTree += OnDetached;
        }));
    }
}
