// File:    ClipboardHelper.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace MasterSafe.App.Views;

/// <summary>Copies a secret to the clipboard and clears it again after 30 seconds if it is still there.</summary>
internal static class ClipboardHelper
{
    private static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(30);
    private static DispatcherTimer? _clearTimer;

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
                ScheduleClear(clipboard, text);
                return true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
            {
                await Task.Delay(100);
            }
        }

        return false;
    }

    private static void ScheduleClear(IClipboard clipboard, string text)
    {
        _clearTimer?.Stop();
        _clearTimer = new DispatcherTimer { Interval = ClearAfter };
        _clearTimer.Tick += async (_, _) =>
        {
            _clearTimer?.Stop();
            try
            {
                if (await clipboard.TryGetTextAsync() == text)
                {
                    await clipboard.ClearAsync();
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
            {
                // The clipboard is busy; the copy simply stays until the next one.
            }
        };
        _clearTimer.Start();
    }
}
