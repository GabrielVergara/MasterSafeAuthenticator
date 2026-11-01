// File:    Argon2Calibrator.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Diagnostics;

namespace MasterSafe.Crypto;

/// <summary>
/// Picks Argon2id settings that suit this computer: as strong as it can afford while one unlock still takes about
/// <c>target</c>. A faster computer gets more memory, which makes guessing on graphics cards and custom hardware costlier.
/// The result is never weaker than <see cref="Argon2Parameters.Default"/>.
/// </summary>
public static class Argon2Calibrator
{
    /// <summary>Most memory the calibration will choose (512 MiB), so an unlock stays possible on modest computers.</summary>
    public const int CeilingKib = 524288;

    /// <summary>How long one unlock should take, about.</summary>
    public static readonly TimeSpan DefaultTarget = TimeSpan.FromMilliseconds(1000);

    /// <summary>Measures how long one derivation takes with the given settings.</summary>
    public static TimeSpan Measure(Argon2Parameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var service = new MasterPasswordService();
        var salt = new byte[16];
        var watch = Stopwatch.StartNew();
        service.DeriveKey("calibration", salt, parameters);
        return watch.Elapsed;
    }

    /// <summary>
    /// Doubles the memory while one derivation stays within the target, up to <see cref="CeilingKib"/>, then adds
    /// iterations while there is still time. <paramref name="measure"/> is replaceable so tests need not wait.
    /// </summary>
    public static Argon2Parameters Calibrate(TimeSpan? target = null, Func<Argon2Parameters, TimeSpan>? measure = null)
    {
        var limit = target ?? DefaultTarget;
        measure ??= Measure;

        var best = Argon2Parameters.Default;
        if (measure(best) > limit)
        {
            return best;
        }

        var candidate = best;
        while (candidate.MemorySizeKib * 2 <= CeilingKib)
        {
            var next = candidate with { MemorySizeKib = candidate.MemorySizeKib * 2 };
            if (measure(next) > limit)
            {
                break;
            }

            candidate = next;
        }

        best = candidate;
        while (best.Iterations < 6)
        {
            var next = best with { Iterations = best.Iterations + 1 };
            if (measure(next) > limit)
            {
                break;
            }

            best = next;
        }

        return best;
    }

    /// <summary>True when <paramref name="calibrated"/> is clearly stronger than <paramref name="current"/>.</summary>
    public static bool IsWorthChanging(Argon2Parameters? current, Argon2Parameters calibrated)
    {
        ArgumentNullException.ThrowIfNull(calibrated);
        return current is null
            || calibrated.MemorySizeKib > current.MemorySizeKib
            || (calibrated.MemorySizeKib == current.MemorySizeKib && calibrated.Iterations > current.Iterations);
    }
}
