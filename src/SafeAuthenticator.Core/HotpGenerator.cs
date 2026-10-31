// File:    HotpGenerator.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Security.Cryptography;

namespace SafeAuthenticator.Core;

/// <summary>
/// RFC 4226 HMAC-based One-Time Password (HOTP) generator.
/// </summary>
public static class HotpGenerator
{
    /// <summary>
    /// Generates an HOTP code for the given secret and counter.
    /// </summary>
    /// <param name="secret">The raw shared secret bytes.</param>
    /// <param name="counter">The moving counter value.</param>
    /// <param name="digits">Number of digits (6 or 8).</param>
    /// <param name="algorithm">HMAC algorithm.</param>
    public static string Generate(ReadOnlySpan<byte> secret, long counter, int digits = 6, OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        if (secret.Length == 0)
        {
            throw new ArgumentException("Secret must not be empty.", nameof(secret));
        }

        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be between 6 and 8.");
        }

        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[64];
        var written = ComputeHmac(algorithm, secret, counterBytes, hash);
        hash = hash[..written];

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, digits);
        var otp = binary % modulo;
        return otp.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private static int ComputeHmac(OtpAlgorithm algorithm, ReadOnlySpan<byte> key, ReadOnlySpan<byte> message, Span<byte> destination)
    {
        return algorithm switch
        {
            OtpAlgorithm.Sha1 => HMACSHA1.HashData(key, message, destination),
            OtpAlgorithm.Sha256 => HMACSHA256.HashData(key, message, destination),
            OtpAlgorithm.Sha512 => HMACSHA512.HashData(key, message, destination),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
    }
}
