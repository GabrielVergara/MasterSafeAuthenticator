// File:    PasswordGenerator.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Security.Cryptography;

namespace SafeAuthenticator.Core;

/// <summary>
/// Generates random passwords using a cryptographically secure RNG.
/// </summary>
public static class PasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";

    /// <summary>Minimum allowed length.</summary>
    public const int MinLength = 8;

    /// <summary>Maximum allowed length.</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Generates a password of the requested length containing at least one character from each enabled set.
    /// </summary>
    public static string Generate(int length = 20, bool includeSymbols = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);

        var sets = includeSymbols ? new[] { Lower, Upper, Digits, Symbols } : [Lower, Upper, Digits];
        var all = string.Concat(sets);
        var chars = new char[length];

        for (var i = 0; i < sets.Length; i++)
        {
            chars[i] = sets[i][RandomNumberGenerator.GetInt32(sets[i].Length)];
        }

        for (var i = sets.Length; i < length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        // Fisher-Yates shuffle so the guaranteed characters are not always first.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
