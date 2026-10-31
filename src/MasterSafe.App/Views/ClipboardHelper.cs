// File:    ClipboardHelper.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using MasterSafe.App.Services;

namespace MasterSafe.App.Views;

/// <summary>
/// Copies a secret to the clipboard and clears it again after <see cref="ClipboardPolicy.ClearAfter"/> if it is still
/// there. Copying another secret restarts the wait.
/// </summary>
internal static class ClipboardHelper
{
    private static readonly ClipboardClearScheduler Scheduler = new(StartTimer);

    /// <summary>Copies <paramref name="text"/>. Returns false when the clipboard could not be written.</summary>
    public static async Task<bool> CopyAsync(Visual visual, string text)
    {
        var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        // Windows lets only one program use the clipboard at a time, so a busy clipboard is retried briefly.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await clipboard.SetTextAsync(text);
                Scheduler.Schedule(text, () => clipboard.TryGetTextAsync(), () => clipboard.ClearAsync());
                return true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
            {
                await Task.Delay(100);
            }
        }

        return false;
    }

    private static IDisposable StartTimer(TimeSpan interval, Func<Task> action)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await action();
        };
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
