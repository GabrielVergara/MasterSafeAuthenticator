// File:    ElapsedClock.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>
/// Measures time that has passed with the system's tick count, which nobody can set, instead of the clock on the screen,
/// which anyone can change. Used for the sign-in pause and the automatic lock, so turning the clock forward does not end a
/// pause and turning it back does not keep the vault open longer.
/// </summary>
/// <param name="ticksMs">Milliseconds since some fixed point that only moves forward. Replaced in tests.</param>
public sealed class ElapsedClock(Func<long> ticksMs)
{
    /// <summary>The system tick count: it keeps counting while Windows sleeps and cannot be changed by the user.</summary>
    public static ElapsedClock System { get; } = new(() => Environment.TickCount64);

    /// <summary>A clock that follows the given time, for tests that move time on by hand.</summary>
    public static ElapsedClock From(Func<DateTimeOffset> clock) => new(() => clock().ToUnixTimeMilliseconds());

    /// <summary>A point in time to measure from.</summary>
    public long Now() => ticksMs();

    /// <summary>Time since <paramref name="mark"/>; never negative.</summary>
    public TimeSpan Since(long mark) => TimeSpan.FromMilliseconds(Math.Max(0, ticksMs() - mark));
}
