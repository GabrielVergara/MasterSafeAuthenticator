// File:    PaperKey.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.Backup;

/// <summary>
/// Turns a recovery key file into pieces that can be printed as QR codes and as text, and back. The key file is already
/// encrypted with its passphrase, so the paper is as safe as the file: both need the passphrase.
/// </summary>
/// <remarks>
/// Each piece reads <c>MSRK1/&lt;n&gt;/&lt;count&gt;/&lt;set&gt;/&lt;check&gt;/&lt;data&gt;</c>. Only digits, capital
/// letters and <c>/</c> are used, so a QR code stores it compactly (alphanumeric mode) and a person can type it with no
/// case to get wrong. <c>set</c> ties the pieces of one key together; <c>check</c> finds a typing mistake in one piece.
/// </remarks>
public static class PaperKey
{
    /// <summary>The start of every piece.</summary>
    public const string Prefix = "MSRK1/";

    /// <summary>
    /// Most data characters per piece. Fits a QR code of 8.5 cm, about 0.6 mm per dot, that a phone photo of the page can
    /// still read. A key is shared evenly between as few pieces as that allows.
    /// </summary>
    public const int MaxPieceLength = 1900;

    private const int MaxPieces = 20;
    private const int MaxKeyFileBytes = 64 * 1024;

    /// <summary>Splits a recovery key file into pieces. The file is checked to be a recovery key first.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a recovery key file.</exception>
    public static IReadOnlyList<string> Split(byte[] keyFile)
    {
        ArgumentNullException.ThrowIfNull(keyFile);
        var data = Base32.Encode(BoundedGzip.Compress(Compact(keyFile, dropNote: true)));
        var set = Hash(data, 4);
        var count = (data.Length + MaxPieceLength - 1) / MaxPieceLength;
        if (count > MaxPieces)
        {
            throw new InvalidDataException("The recovery key file is too large to print.");
        }

        var length = (data.Length + count - 1) / count;
        var pieces = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var part = data.Substring(i * length, Math.Min(length, data.Length - (i * length)));
            pieces.Add($"{Prefix}{i + 1}/{count}/{set}/{Check(i + 1, count, set, part)}/{part}");
        }

        return pieces;
    }

    /// <summary>
    /// Puts the recovery key file back together from text read from QR codes or typed in. Spaces, line breaks and
    /// repeated pieces are ignored; pieces can come in any order.
    /// </summary>
    /// <exception cref="InvalidDataException">A piece is missing or wrong; the message says which.</exception>
    public static byte[] Join(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var all = new StringBuilder();
        foreach (var text in texts)
        {
            foreach (var c in text ?? string.Empty)
            {
                if (!char.IsWhiteSpace(c))
                {
                    all.Append(char.ToUpperInvariant(c));
                }

                if (all.Length > MaxPieces * (MaxPieceLength + 64))
                {
                    throw new InvalidDataException("That is more text than a printed recovery key has.");
                }
            }
        }

        string? set = null;
        var count = 0;
        var parts = new Dictionary<int, string>();
        foreach (var piece in all.ToString().Split(Prefix, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = piece.Split('/');
            if (fields.Length != 5 || !int.TryParse(fields[0], out var n) || !int.TryParse(fields[1], out var total)
                || total is < 1 or > MaxPieces || n < 1 || n > total)
            {
                throw new InvalidDataException("Part of the text is not a piece of a printed recovery key.");
            }

            if (Check(n, total, fields[2], fields[4]) != fields[3])
            {
                throw new InvalidDataException($"Piece {n} has a mistake. Check that piece letter by letter, or read its code again.");
            }

            if (set is not null && (set != fields[2] || count != total))
            {
                throw new InvalidDataException("These pieces come from different recovery keys. Use the pieces of one sheet only.");
            }

            set = fields[2];
            count = total;
            parts[n] = fields[4];
        }

        if (set is null)
        {
            throw new InvalidDataException("No piece of a printed recovery key was found.");
        }

        var missing = Enumerable.Range(1, count).Where(n => !parts.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"Missing pieces: {string.Join(", ", missing)} of {count}.");
        }

        var data = string.Concat(Enumerable.Range(1, count).Select(n => parts[n]));
        if (Hash(data, 4) != set)
        {
            throw new InvalidDataException("The pieces do not fit together. Use the pieces of one sheet only.");
        }

        try
        {
            var file = BoundedGzip.Decompress(Base32.Decode(data), MaxKeyFileBytes);
            Compact(file);
            return file;
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The printed recovery key could not be read.", ex);
        }
    }

    // The file without indentation, checked to be a recovery key. The note for people reading the file is left out on
    // paper (it comes back as the default when the file is read) and is not part of what the passphrase protects.
    // Every other field is kept, so nothing that matters is lost.
    private static byte[] Compact(byte[] keyFile, bool dropNote = false)
    {
        if (keyFile.Length > MaxKeyFileBytes)
        {
            throw new InvalidDataException("This is not a MasterSafe recovery key file.");
        }

        try
        {
            using var doc = JsonDocument.Parse(keyFile);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(nameof(RecoveryKeyFile.WrappedPrivateKey), out var wrapped) || wrapped.ValueKind != JsonValueKind.String
                || !root.TryGetProperty(nameof(RecoveryKeyFile.KeyId), out var id) || id.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("This is not a MasterSafe recovery key file.");
            }

            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (!(dropNote && property.NameEquals(nameof(RecoveryKeyFile.Note))))
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            return output.ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This is not a MasterSafe recovery key file.", ex);
        }
    }

    private static string Check(int n, int count, string set, string data) => Hash($"{n}/{count}/{set}/{data}", 4);

    private static string Hash(string text, int length)
        => Base32.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(text)))[..length];
}
