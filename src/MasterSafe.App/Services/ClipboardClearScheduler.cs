// File:    ClipboardClearScheduler.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>One of the waits the user can choose in Settings.</summary>
/// <param name="Seconds">The wait in seconds.</param>
/// <param name="Label">How it is shown, for example "2 minutes".</param>
public sealed record ClipboardDelayOption(int Seconds, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>How long a copied secret stays on the clipboard. The user chooses one of three waits in Settings.</summary>
public static class ClipboardPolicy
{
    /// <summary>The wait used when nothing was chosen (2 minutes).</summary>
    public const int DefaultSeconds = 120;

    /// <summary>The waits that can be chosen.</summary>
    public static IReadOnlyList<ClipboardDelayOption> Options { get; } =
    [
        new(30, "30 seconds"),
        new(60, "1 minute"),
        new(120, "2 minutes"),
    ];

    private static int _seconds = DefaultSeconds;

    /// <summary>The clipboard is cleared this long after the last copy of a secret.</summary>
    public static TimeSpan ClearAfter => TimeSpan.FromSeconds(_seconds);

    /// <summary>The wait as it is written in messages and help, for example "2 minutes".</summary>
    public static string ClearAfterText => Options.First(o => o.Seconds == _seconds).Label;

    /// <summary>The chosen option.</summary>
    public static ClipboardDelayOption Current => Options.First(o => o.Seconds == _seconds);

    /// <summary>Returns <paramref name="seconds"/> when it is one of the allowed waits, otherwise the default. Settings files can hold anything.</summary>
    public static int Normalize(int seconds) => Options.Any(o => o.Seconds == seconds) ? seconds : DefaultSeconds;

    /// <summary>Uses a wait (an unknown value gives the default).</summary>
    public static void Use(int seconds) => _seconds = Normalize(seconds);
}

/// <summary>
/// Clears the clipboard some time after a secret was copied. Copying another secret starts the wait again from zero,
/// so the newest copy always gets the full time. The clipboard is only cleared if it still holds the copied text, so
/// something else the user copied in the meantime is never wiped.
/// </summary>
/// <param name="startTimer">Starts a one-shot timer that runs the action when it ends; disposing the result cancels it.</param>
public sealed class ClipboardClearScheduler(Func<TimeSpan, Func<Task>, IDisposable> startTimer)
{
    private const int ClearAttempts = 5;

    private IDisposable? _timer;
    private Func<Task>? _pending;
    private int _generation;

    /// <summary>True while a clear is waiting to happen.</summary>
    public bool IsPending => _timer is not null;

    /// <summary>Starts (or restarts) the wait for a secret that was just copied.</summary>
    /// <param name="text">What was copied.</param>
    /// <param name="read">Reads what is on the clipboard now.</param>
    /// <param name="clear">Empties the clipboard.</param>
    /// <param name="after">The wait; <see cref="ClipboardPolicy.ClearAfter"/> when not given.</param>
    public void Schedule(string text, Func<Task<string?>> read, Func<Task> clear, TimeSpan? after = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(clear);

        Cancel();
        var generation = _generation;
        _pending = () => ClearIfStillThereAsync(text, read, clear);

        _timer = startTimer(after ?? ClipboardPolicy.ClearAfter, async () =>
        {
            // A timer that was replaced in the meantime must not clear anything.
            if (generation != _generation)
            {
                return;
            }

            await TakePending();
        });
    }

    /// <summary>Clears a waiting secret straight away, for when the vault locks or the program closes.</summary>
    public Task ClearNowAsync() => TakePending();

    /// <summary>Stops waiting without clearing anything.</summary>
    public void Cancel()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        _pending = null;
    }

    private Task TakePending()
    {
        var pending = _pending;
        Cancel();
        return pending?.Invoke() ?? Task.CompletedTask;
    }

    private static async Task ClearIfStillThereAsync(string text, Func<Task<string?>> read, Func<Task> clear)
    {
        // Windows lets only one program use the clipboard at a time, and this is the last chance to clear it.
        for (var attempt = 0; attempt < ClearAttempts; attempt++)
        {
            try
            {
                if (await read() == text)
                {
                    await clear();
                }

                return;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
            {
                await Task.Delay(100);
            }
        }
    }
}
