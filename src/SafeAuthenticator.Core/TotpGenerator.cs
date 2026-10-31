namespace SafeAuthenticator.Core;

/// <summary>
/// RFC 6238 Time-based One-Time Password (TOTP) generator.
/// </summary>
public static class TotpGenerator
{
    /// <summary>Unix epoch reference used by RFC 6238.</summary>
    public static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// Generates a TOTP code for the supplied moment in time.
    /// </summary>
    /// <param name="secret">Raw shared secret bytes.</param>
    /// <param name="timestamp">The instant to generate the code for.</param>
    /// <param name="period">Time step in seconds (e.g. 30, 60 or custom).</param>
    /// <param name="digits">Number of digits (6 or 8).</param>
    /// <param name="algorithm">HMAC algorithm.</param>
    public static string Generate(
        ReadOnlySpan<byte> secret,
        DateTimeOffset timestamp,
        int period = 30,
        int digits = 6,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be positive.");
        }

        var counter = CounterFor(timestamp, period);
        return HotpGenerator.Generate(secret, counter, digits, algorithm);
    }

    /// <summary>
    /// Returns the number of seconds remaining in the current time step.
    /// </summary>
    public static int RemainingSeconds(DateTimeOffset timestamp, int period = 30)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be positive.");
        }

        var seconds = timestamp.ToUnixTimeSeconds();
        return (int)(period - (seconds % period));
    }

    /// <summary>
    /// Computes the time-step counter for a timestamp.
    /// </summary>
    public static long CounterFor(DateTimeOffset timestamp, int period)
    {
        var seconds = timestamp.ToUnixTimeSeconds();
        return seconds / period;
    }
}
