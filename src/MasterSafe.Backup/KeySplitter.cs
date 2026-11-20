// File:    KeySplitter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text.Json;

namespace MasterSafe.Backup;

/// <summary>One of the parts a recovery key file is split into.</summary>
public sealed class KeyPart
{
    /// <summary>Format version (1).</summary>
    public int Version { get; init; } = 1;

    /// <summary>A note shown to anyone who opens the file in a text editor.</summary>
    public string Note { get; init; } = "Part of a MasterSafe Authenticator recovery key. Any 2 of the 3 parts rebuild the key, which still needs its passphrase. Keep each part in a different place.";

    /// <summary>Random identifier shared by the parts of one split (Base64).</summary>
    public string SetId { get; init; } = string.Empty;

    /// <summary>The number of this part: 1, 2 or 3.</summary>
    public int Index { get; init; }

    /// <summary>How many parts are needed (2).</summary>
    public int Threshold { get; init; }

    /// <summary>How many parts were made (3).</summary>
    public int Count { get; init; }

    /// <summary>SHA-256 of the whole key file (Base64), to check the rebuilt key. The key file is encrypted, so this tells nothing.</summary>
    public string KeyHash { get; init; } = string.Empty;

    /// <summary>This part's share of the key file (Base64).</summary>
    public string Data { get; init; } = string.Empty;
}

/// <summary>
/// Splits a recovery key file into 3 parts so that any 2 rebuild it and 1 alone says nothing about it (Shamir's secret
/// sharing over GF(2^8), one random line per byte). The key file stays locked with its passphrase, so even 2 parts in the
/// wrong hands are not enough without the passphrase.
/// </summary>
public static class KeySplitter
{
    /// <summary>Parts made.</summary>
    public const int Count = 3;

    /// <summary>Parts needed.</summary>
    public const int Threshold = 2;

    private const int MaxPartFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Splits <paramref name="keyFile"/> into <see cref="Count"/> part files.</summary>
    public static IReadOnlyList<byte[]> Split(byte[] keyFile)
    {
        ArgumentNullException.ThrowIfNull(keyFile);
        if (keyFile.Length == 0)
        {
            throw new ArgumentException("The key file is empty.", nameof(keyFile));
        }

        var slope = RandomNumberGenerator.GetBytes(keyFile.Length);
        var setId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = Convert.ToBase64String(SHA256.HashData(keyFile));
        try
        {
            var parts = new List<byte[]>(Count);
            for (byte x = 1; x <= Count; x++)
            {
                // f(x) = secret + slope * x, byte by byte.
                var share = new byte[keyFile.Length];
                for (var i = 0; i < share.Length; i++)
                {
                    share[i] = (byte)(keyFile[i] ^ Multiply(slope[i], x));
                }

                parts.Add(JsonSerializer.SerializeToUtf8Bytes(
                    new KeyPart { SetId = setId, Index = x, Threshold = Threshold, Count = Count, KeyHash = hash, Data = Convert.ToBase64String(share) },
                    JsonOptions));
            }

            return parts;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(slope);
        }
    }

    /// <summary>Rebuilds the key file from part files. Two different parts of the same split are enough; more are checked too.</summary>
    /// <exception cref="InvalidDataException">The parts are not enough, damaged, or from different splits.</exception>
    public static byte[] Combine(IEnumerable<byte[]> partFiles)
    {
        ArgumentNullException.ThrowIfNull(partFiles);

        var parts = new Dictionary<int, (KeyPart Part, byte[] Share)>();
        foreach (var file in partFiles)
        {
            var part = Parse(file);
            var share = Convert.FromBase64String(part.Data);
            var first = parts.Values.FirstOrDefault().Part;
            if (first is not null && (first.SetId != part.SetId || first.KeyHash != part.KeyHash))
            {
                throw new InvalidDataException("These parts come from different recovery keys. Use parts of one split only.");
            }

            if (first is not null && Convert.FromBase64String(first.Data).Length != share.Length)
            {
                throw new InvalidDataException("A key part is damaged.");
            }

            parts[part.Index] = (part, share);
        }

        if (parts.Count < Threshold)
        {
            throw new InvalidDataException($"Add one more key part: any {Threshold} of the {Count} parts are needed.");
        }

        var (a, b) = (parts.First(), parts.Skip(1).First());
        var key = Interpolate((byte)a.Key, a.Value.Share, (byte)b.Key, b.Value.Share);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(key), Convert.FromBase64String(a.Value.Part.KeyHash)))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("The key parts do not fit together. One of them is damaged or from another key.");
        }

        // A third part, when given, must agree too: a damaged one is reported rather than ignored.
        foreach (var extra in parts.Skip(2))
        {
            var check = Interpolate((byte)a.Key, a.Value.Share, (byte)extra.Key, extra.Value.Share);
            var same = CryptographicOperations.FixedTimeEquals(check, key);
            CryptographicOperations.ZeroMemory(check);
            if (!same)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidDataException($"Key part {extra.Key} is damaged. Use the other two.");
            }
        }

        return key;
    }

    private static KeyPart Parse(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length > MaxPartFileBytes)
        {
            throw new InvalidDataException("This is not a MasterSafe recovery key part.");
        }

        try
        {
            var part = JsonSerializer.Deserialize<KeyPart>(file);
            if (part is null || part.Version != 1 || part.Threshold != Threshold || part.Count != Count
                || part.Index is < 1 or > Count || part.SetId.Length == 0 || part.KeyHash.Length == 0 || part.Data.Length == 0)
            {
                throw new InvalidDataException("This is not a MasterSafe recovery key part.");
            }

            return part;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new InvalidDataException("This is not a MasterSafe recovery key part.", ex);
        }
    }

    // The value at x = 0 of the line through (x1, y1) and (x2, y2): (y1*x2 + y2*x1) / (x1 + x2), in GF(2^8).
    private static byte[] Interpolate(byte x1, byte[] y1, byte x2, byte[] y2)
    {
        var inverse = Inverse((byte)(x1 ^ x2));
        var result = new byte[y1.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Multiply((byte)(Multiply(y1[i], x2) ^ Multiply(y2[i], x1)), inverse);
        }

        return result;
    }

    // Multiplication in GF(2^8) with the AES polynomial, without lookup tables or branches on the data.
    private static byte Multiply(byte a, byte b)
    {
        int product = 0, x = a, y = b;
        for (var bit = 0; bit < 8; bit++)
        {
            product ^= x & -(y & 1);
            y >>= 1;
            x = (x << 1) ^ (0x11B & -((x >> 7) & 1));
        }

        return (byte)product;
    }

    // a^254 is the inverse of a in GF(2^8). Only used on the public x values.
    private static byte Inverse(byte a)
    {
        byte result = 1;
        for (var i = 0; i < 254; i++)
        {
            result = Multiply(result, a);
        }

        return result;
    }
}
