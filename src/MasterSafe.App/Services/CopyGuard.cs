// File:    CopyGuard.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.App.Services;

/// <summary>
/// Slows down copying many secrets in a short time. A person copies a few; a script or an AI agent driving the open
/// program could copy every entry in a minute. After <see cref="Limit"/> copies within <see cref="Window"/>, copying pauses
/// for <see cref="Pause"/>, and the pause is recorded as a security event. This does not stop a determined attacker who
/// already controls the computer, but it turns a minute into hours and leaves a trace.
/// </summary>
public sealed class CopyGuard(Func<DateTimeOffset> clock)
{
    /// <summary>Copies allowed within the window.</summary>
    public const int Limit = 20;

    /// <summary>The window the copies are counted in.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>How long copying stays paused after the limit.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(30);

    private readonly Queue<DateTimeOffset> _recent = new();
    private DateTimeOffset _pausedUntil;

    /// <summary>The guard the program uses.</summary>
    public static CopyGuard Shared { get; } = new(() => DateTimeOffset.UtcNow);

    /// <summary>Raised once each time copying pauses.</summary>
    public event EventHandler? Paused;

    /// <summary>True while copying is paused.</summary>
    public bool IsPaused => clock() < _pausedUntil;

    /// <summary>Counts a copy. Returns false when it must not happen now.</summary>
    public bool TryCopy()
    {
        var now = clock();
        if (now < _pausedUntil)
        {
            return false;
        }

        while (_recent.Count > 0 && now - _recent.Peek() > Window)
        {
            _recent.Dequeue();
        }

        if (_recent.Count >= Limit)
        {
            _recent.Clear();
            _pausedUntil = now + Pause;
            Paused?.Invoke(this, EventArgs.Empty);
            return false;
        }

        _recent.Enqueue(now);
        return true;
    }
}
