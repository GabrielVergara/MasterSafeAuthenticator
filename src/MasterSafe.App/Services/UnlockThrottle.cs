// File:    UnlockThrottle.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;

namespace MasterSafe.App.Services;

/// <summary>
/// Slows down wrong master passwords at the sign-in screen. The first few mistakes cost nothing; after
/// <see cref="FreeAttempts"/> wrong passwords in a row the screen pauses, and every further mistake makes the pause longer
/// (30 seconds, 1, 2, 5 and finally 15 minutes). A correct password resets it.
/// <para>
/// This protects against someone guessing at your keyboard. It cannot stop someone who copies the vault file and guesses
/// offline: that is what the slow Argon2id key derivation and a long master password are for. The counter is kept in the
/// settings file so closing the app does not reset it, but a person with access to your files can edit that file.
/// Nothing is ever deleted because of wrong passwords.
/// </para>
/// <para>
/// The pause is timed with <see cref="ElapsedClock"/>, so changing the computer's clock does not end it. A second counter,
/// <see cref="TotalFailures"/>, only goes up; the vault remembers the value it last saw, so a counter that was turned back
/// in the settings file is noticed at the next sign-in. Putting back an exact earlier copy of the file is not noticed.
/// </para>
/// </summary>
public sealed class UnlockThrottle
{
    /// <summary>Wrong passwords in a row that are allowed before the first pause.</summary>
    public const int FreeAttempts = 5;

    private static readonly TimeSpan[] Pauses =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    private readonly Func<DateTimeOffset> _clock;
    private readonly ElapsedClock _elapsed;
    private readonly ISettingsStore? _store;

    // The pause running now: when it started on the elapsed clock and how long it is.
    private long _pauseStart;
    private TimeSpan _pause;

    /// <summary>Loads the saved counter (when a store is given) so it survives restarts.</summary>
    /// <param name="clock">The wall clock, only used to keep a pause across a restart.</param>
    /// <param name="store">Where the counter is kept.</param>
    /// <param name="elapsed">Times the pause; by default it follows <paramref name="clock"/>, which tests move by hand.</param>
    public UnlockThrottle(Func<DateTimeOffset> clock, ISettingsStore? store = null, ElapsedClock? elapsed = null)
    {
        _clock = clock;
        _elapsed = elapsed ?? ElapsedClock.From(clock);
        _store = store;
        if (store is null)
        {
            return;
        }

        var saved = store.Load();
        Failures = Math.Clamp(saved.UnlockFailures, 0, 1000);
        TotalFailures = Math.Max(Math.Max(saved.UnlockFailuresTotal, 0), Failures);
        if (DateTimeOffset.TryParse(saved.UnlockBlockedUntilUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until)
            && until > clock())
        {
            // A settings file or a clock that was changed cannot make the pause longer than the longest one.
            StartPause(until - clock() > Pauses[^1] ? Pauses[^1] : until - clock());
        }
    }

    /// <summary>Wrong master passwords since the last correct one.</summary>
    public int Failures { get; private set; }

    /// <summary>Every wrong master password on this computer. It never goes down, so a counter that was turned back shows.</summary>
    public long TotalFailures { get; private set; }

    /// <summary>True while sign-in with the master password is paused.</summary>
    public bool IsBlocked => Remaining > TimeSpan.Zero;

    /// <summary>How long the pause lasts from now; zero when there is none.</summary>
    public TimeSpan Remaining => _pause > TimeSpan.Zero && _pause > _elapsed.Since(_pauseStart) ? _pause - _elapsed.Since(_pauseStart) : TimeSpan.Zero;

    private void StartPause(TimeSpan pause)
    {
        _pauseStart = _elapsed.Now();
        _pause = pause;
    }

    /// <summary>Wrong passwords that are still allowed before the next pause starts (0 once a pause has started).</summary>
    public int AttemptsLeft => Math.Max(0, FreeAttempts - Failures);

    /// <summary>Records a wrong password and starts a pause when the free attempts are used up.</summary>
    public void RecordFailure()
    {
        Failures = Math.Min(Failures + 1, 1000);
        TotalFailures = Math.Min(TotalFailures + 1, long.MaxValue / 2);
        if (Failures >= FreeAttempts)
        {
            StartPause(Pauses[Math.Min(Failures - FreeAttempts, Pauses.Length - 1)]);
        }

        Save();
    }

    /// <summary>Records a correct unlock and returns how many wrong passwords came before it.</summary>
    public int RecordSuccess()
    {
        var before = Failures;
        Failures = 0;
        _pause = TimeSpan.Zero;
        Save();
        return before;
    }

    /// <summary>The remaining pause for people, for example "27 seconds" or "4 minutes".</summary>
    public static string Describe(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(span.TotalSeconds);
        return seconds < 90
            ? $"{Math.Max(1, seconds)} second{(seconds == 1 ? string.Empty : "s")}"
            : $"{(int)Math.Ceiling(seconds / 60.0)} minutes";
    }

    private void Save()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            var settings = _store.Load();
            settings.UnlockFailures = Failures;
            settings.UnlockFailuresTotal = TotalFailures;
            settings.UnlockBlockedUntilUtc = IsBlocked ? (_clock() + Remaining).ToString("O", CultureInfo.InvariantCulture) : null;
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a writable settings file the counter simply lives until the app is closed.
        }
    }
}
