// File:    VaultContentsCodec.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using System.Text.Json;
using MasterSafe.Core;

namespace MasterSafe.Crypto;

/// <summary>
/// Turns <see cref="VaultContents"/> into the bytes that are compressed and encrypted, and back.
/// <para>
/// A vault without attachments is plain JSON, exactly as before, so older versions can still open it. A vault with
/// attachments is a small container: the marker <c>MSV4</c>, the length of the JSON, the JSON, the number of files and
/// then each file (its id, its length and its bytes). File bytes are not put inside the JSON because that would
/// make them a third larger.
/// </para>
/// Everything read from here comes from a file, so every length is checked before memory is reserved.
/// </summary>
public static class VaultContentsCodec
{
    private static readonly byte[] Magic = "MSV4"u8.ToArray();

    /// <summary>Largest JSON part accepted (16 MiB).</summary>
    public const int MaxJsonBytes = 16 * 1024 * 1024;

    /// <summary>Serialises the contents. The caller should wipe the result when it is done with it.</summary>
    /// <exception cref="InvalidDataException">The attachments exceed the limits.</exception>
    public static byte[] Encode(VaultContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var files = ReferencedFiles(contents);
        var json = JsonSerializer.SerializeToUtf8Bytes(contents);
        if (files.Count == 0)
        {
            return json;
        }

        try
        {
            using var output = new MemoryStream();
            output.Write(Magic);
            WriteInt32(output, json.Length);
            output.Write(json);
            WriteInt32(output, files.Count);
            foreach (var (id, data) in files)
            {
                output.Write(id.ToByteArray());
                WriteInt32(output, data.Length);
                output.Write(data);
            }

            return output.ToArray();
        }
        finally
        {
            Array.Clear(json);
        }
    }

    /// <summary>Reads contents written by <see cref="Encode"/>.</summary>
    /// <exception cref="InvalidDataException">The data is damaged or exceeds a limit.</exception>
    public static VaultContents Decode(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(Magic))
        {
            var plain = Deserialize(data);
            return plain.Passwords.Any(p => p.Attachments.Count > 0) ? With(plain, []) : plain;
        }

        var position = Magic.Length;
        var jsonLength = ReadInt32(data, ref position);
        if (jsonLength < 0 || jsonLength > MaxJsonBytes || jsonLength > data.Length - position)
        {
            throw new InvalidDataException("The vault contents are damaged.");
        }

        var contents = Deserialize(data.Slice(position, jsonLength));
        position += jsonLength;

        var count = ReadInt32(data, ref position);
        if (count < 0 || count > AttachmentLimits.MaxCount)
        {
            throw new InvalidDataException("The vault has too many attachments.");
        }

        var files = new Dictionary<Guid, byte[]>(count);
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            if (data.Length - position < 16)
            {
                throw new InvalidDataException("The vault contents are damaged.");
            }

            var id = new Guid(data.Slice(position, 16));
            position += 16;

            var length = ReadInt32(data, ref position);
            total += length;
            if (length < 0 || length > AttachmentLimits.MaxFileBytes || total > AttachmentLimits.MaxTotalBytes || length > data.Length - position)
            {
                throw new InvalidDataException("An attachment in the vault is damaged or too large.");
            }

            files[id] = data.Slice(position, length).ToArray();
            position += length;
        }

        return With(contents, files);
    }

    private static VaultContents Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<VaultContents>(json) ?? new VaultContents();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The vault contents are damaged.", ex);
        }
    }

    /// <summary>
    /// Keeps only the files that an entry refers to and whose size matches, and drops the description of an
    /// attachment whose file is missing, so nothing points at nothing.
    /// </summary>
    private static VaultContents With(VaultContents contents, Dictionary<Guid, byte[]> files)
    {
        var kept = new Dictionary<Guid, byte[]>();
        var passwords = contents.Passwords.Select(entry =>
        {
            if (entry.Attachments.Count == 0)
            {
                return entry;
            }

            var good = entry.Attachments
                .Where(a => files.TryGetValue(a.Id, out var bytes) && bytes.Length == a.Size)
                .Select(a => new PasswordAttachment { Id = a.Id, Name = AttachmentLimits.SafeName(a.Name), Size = a.Size })
                .ToList();
            foreach (var attachment in good)
            {
                kept[attachment.Id] = files[attachment.Id];
            }

            return entry.WithAttachments(good);
        }).ToList();

        return new VaultContents
        {
            Accounts = contents.Accounts,
            Passwords = passwords,
            Folders = contents.Folders,
            FolderIcons = contents.FolderIcons,
            RecoveryKeyFingerprint = contents.RecoveryKeyFingerprint,
            AttachmentData = kept,
        };
    }

    private static List<KeyValuePair<Guid, byte[]>> ReferencedFiles(VaultContents contents)
    {
        var files = new List<KeyValuePair<Guid, byte[]>>();
        var seen = new HashSet<Guid>();
        long total = 0;
        foreach (var attachment in contents.Passwords.SelectMany(p => p.Attachments))
        {
            if (!seen.Add(attachment.Id))
            {
                continue;
            }

            if (!contents.AttachmentData.TryGetValue(attachment.Id, out var data))
            {
                throw new InvalidDataException($"The file '{attachment.Name}' is missing from the vault contents.");
            }

            total += data.Length;
            if (data.Length > AttachmentLimits.MaxFileBytes || total > AttachmentLimits.MaxTotalBytes || files.Count >= AttachmentLimits.MaxCount)
            {
                throw new InvalidDataException("The attachments are larger than the vault allows.");
            }

            files.Add(new KeyValuePair<Guid, byte[]>(attachment.Id, data));
        }

        return files;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int position)
    {
        if (data.Length - position < 4)
        {
            throw new InvalidDataException("The vault contents are damaged.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(position, 4));
        position += 4;
        return value;
    }
}
