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

    /// <summary>Why the last copy did not happen, for the status line.</summary>
    public static string FailureMessage => Services.CopyGuard.Shared.IsPaused
        ? "Many secrets were copied in a short time, so copying is paused for 30 seconds. If this was not you, lock the vault now."
        : "Could not copy: the clipboard is in use by another program. Try again.";

    /// <summary>Copies <paramref name="text"/>. Returns false when the clipboard could not be written or copying is paused.</summary>
    public static async Task<bool> CopyAsync(Visual visual, string text)
    {
        var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
        if (clipboard is null || !Services.CopyGuard.Shared.TryCopy())
        {
            return false;
        }

        // Windows lets only one program use the clipboard at a time, so a busy clipboard is retried briefly.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // Not SetTextAsync: the markers keep Windows from putting the secret in its clipboard history.
                await clipboard.SetDataAsync(SecretClipboard.Create(text));
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

    /// <summary>Clears a copied secret now, if it is still on the clipboard.</summary>
    public static Task ClearNowAsync() => Scheduler.ClearNowAsync();

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
