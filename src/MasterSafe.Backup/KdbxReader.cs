// File:    KdbxReader.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MasterSafe.Core.Import;

namespace MasterSafe.Backup;

/// <summary>
/// Opens a KeePass database (KDBX 3.1 and 4.x) with its password and optional key file, read only, and returns its XML
/// with the protected values decrypted, for <see cref="KeePassImporter.ParseXml"/>. Written from the public format
/// description; KeePass's own library is GPL-licensed and is not used.
/// </summary>
/// <remarks>
/// The file is untrusted. Every length is checked before it is used, the key-derivation cost is limited before any work
/// is done, the decompressed size is bounded and the XML parser refuses DTDs. A wrong password is detected by the header
/// HMAC (4.x) or the stream start bytes (3.1) before anything is decrypted further.
/// </remarks>
public static class KdbxReader
{
    /// <summary>Largest decompressed database that is read.</summary>
    public const int MaxXmlBytes = 128 * 1024 * 1024;

    /// <summary>Most AES-KDF rounds accepted. KeePass's default is about 60,000 for old files and a few million for tuned ones.</summary>
    public const ulong MaxAesRounds = 30_000_000;

    /// <summary>Most Argon2 memory accepted, 1 GiB, as for MasterSafe's own vaults.</summary>
    public const ulong MaxArgon2Bytes = 1024UL * 1024 * 1024;

    private const uint SignatureOne = 0x9AA2D903;
    private const uint SignatureTwo = 0xB54BFB67;

    private static readonly byte[] Aes256 = Convert.FromHexString("31C1F2E6BF714350BE5805216AFC5AFF");
    private static readonly byte[] ChaCha20Cipher = Convert.FromHexString("D6038A2B8B6F4CB5A524339A31DBB59A");
    private static readonly byte[] AesKdf = Convert.FromHexString("C9D9F39A628A4460BF740D08C18A4FEA");
    private static readonly byte[] AesKdfOld = Convert.FromHexString("7C02BB8279A74AC0927D114A00648238");
    private static readonly byte[] Argon2d = Convert.FromHexString("EF636DDF8C29444B91F7A9A403E30A0C");
    private static readonly byte[] Argon2id = Convert.FromHexString("9E298B1956DB4773B23DFC3EC6F0A1E6");
    private static readonly byte[] Salsa20Nonce = Convert.FromHexString("E830094B97205D2A");

    /// <summary>True when the bytes start like a KeePass 2 database.</summary>
    public static bool IsKdbx(ReadOnlySpan<byte> data)
        => data.Length >= 8
           && BinaryPrimitives.ReadUInt32LittleEndian(data) == SignatureOne
           && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == SignatureTwo;

    /// <summary>Opens the database and returns its readable XML.</summary>
    /// <exception cref="KeePassImportException">The file is damaged, unsupported, or the password or key file is wrong.</exception>
    public static string ReadXml(byte[] file, string? password, byte[]? keyFile)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!IsKdbx(file))
        {
            throw new KeePassImportException("This is not a KeePass database.");
        }

        if (string.IsNullOrEmpty(password) && keyFile is null)
        {
            throw new KeePassImportException("Enter the database's password, choose its key file, or both.");
        }

        try
        {
            var version = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8));
            var major = version >> 16;
            return major switch
            {
                4 => ReadVersion4(file, CompositeKey(password, keyFile)),
                3 => ReadVersion3(file, CompositeKey(password, keyFile)),
                _ => throw new KeePassImportException($"KeePass database version {major} is not supported. Save it with KeePass 2.x or KeePassXC first."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException or EndOfStreamException or OverflowException or XmlException or FormatException)
        {
            throw new KeePassImportException("The KeePass database is damaged or not in a format MasterSafe can read.");
        }
    }

    // ---------------------------------------------------------------- keys

    private static byte[] CompositeKey(string? password, byte[]? keyFile)
    {
        using var parts = new MemoryStream();
        if (!string.IsNullOrEmpty(password))
        {
            var bytes = Encoding.UTF8.GetBytes(password);
            parts.Write(SHA256.HashData(bytes));
            CryptographicOperations.ZeroMemory(bytes);
        }

        if (keyFile is not null)
        {
            parts.Write(KeyFileHash(keyFile));
        }

        return SHA256.HashData(parts.ToArray());
    }

    /// <summary>The 32 bytes a key file stands for: XML (version 1 or 2), 32 raw bytes, 64 hex digits, or else its SHA-256.</summary>
    public static byte[] KeyFileHash(byte[] keyFile)
    {
        if (keyFile.Length > 1024 * 1024)
        {
            throw new KeePassImportException("The key file is larger than 1 MiB, so it is not a KeePass key file.");
        }

        if (keyFile.Length == 32)
        {
            return (byte[])keyFile.Clone();
        }

        var text = Encoding.UTF8.GetString(keyFile).Trim().TrimStart('﻿');
        if (text.Length == 64 && text.All(Uri.IsHexDigit))
        {
            return Convert.FromHexString(text);
        }

        if (text.StartsWith('<'))
        {
            try
            {
                var document = XDocument.Load(XmlReader.Create(new StringReader(text), SafeXml(1024 * 1024)));
                var key = document.Root?.Element("Key");
                var data = key?.Element("Data");
                var version = document.Root?.Element("Meta")?.Element("Version")?.Value ?? "1.0";
                if (data is not null)
                {
                    if (version.StartsWith('2'))
                    {
                        var hex = string.Concat(data.Value.Where(c => !char.IsWhiteSpace(c)));
                        var bytes = Convert.FromHexString(hex);
                        var expected = (string?)data.Attribute("Hash");
                        if (expected is not null && !Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 4)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new KeePassImportException("The key file is damaged: its check value does not match.");
                        }

                        return bytes;
                    }

                    return Convert.FromBase64String(data.Value.Trim());
                }
            }
            catch (XmlException)
            {
                // Not a KeePass XML key file: any file can be a key file, hashed below.
            }
        }

        return SHA256.HashData(keyFile);
    }

    private static byte[] TransformKey(byte[] composite, Dictionary<string, (byte Type, byte[] Value)> kdf)
    {
        var uuid = Get(kdf, "$UUID");
        if (uuid.SequenceEqual(AesKdf) || uuid.SequenceEqual(AesKdfOld))
        {
            return AesTransform(composite, Get(kdf, "S"), BinaryPrimitives.ReadUInt64LittleEndian(Get(kdf, "R")));
        }

        var isArgon2d = uuid.SequenceEqual(Argon2d);
        if (!isArgon2d && !uuid.SequenceEqual(Argon2id))
        {
            throw new KeePassImportException("This database uses a key derivation MasterSafe does not support.");
        }

        var memory = BinaryPrimitives.ReadUInt64LittleEndian(Get(kdf, "M"));
        var iterations = BinaryPrimitives.ReadUInt64LittleEndian(Get(kdf, "I"));
        var parallelism = BinaryPrimitives.ReadUInt32LittleEndian(Get(kdf, "P"));
        if (memory is < 8 * 1024 or > MaxArgon2Bytes || iterations is < 1 or > 100 || parallelism is < 1 or > 64)
        {
            throw new KeePassImportException("The database asks for more memory or time to open than MasterSafe allows (up to 1 GiB).");
        }

        Konscious.Security.Cryptography.Argon2 argon2 = isArgon2d
            ? new Konscious.Security.Cryptography.Argon2d(composite)
            : new Konscious.Security.Cryptography.Argon2id(composite);
        using (argon2)
        {
            argon2.Salt = Get(kdf, "S");
            argon2.MemorySize = (int)(memory / 1024);
            argon2.Iterations = (int)iterations;
            argon2.DegreeOfParallelism = (int)parallelism;
            if (kdf.TryGetValue("K", out var secret))
            {
                argon2.KnownSecret = secret.Value;
            }

            if (kdf.TryGetValue("A", out var associated))
            {
                argon2.AssociatedData = associated.Value;
            }

            return argon2.GetBytes(32);
        }
    }

    private static byte[] AesTransform(byte[] composite, byte[] seed, ulong rounds)
    {
        if (seed.Length != 32 || rounds is 0 or > MaxAesRounds)
        {
            throw new KeePassImportException("The database asks for more time to open than MasterSafe allows.");
        }

        using var aes = Aes.Create();
        aes.Key = seed;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var transform = aes.CreateEncryptor();
        var block = (byte[])composite.Clone();
        for (ulong i = 0; i < rounds; i++)
        {
            transform.TransformBlock(block, 0, 32, block, 0);
        }

        var result = SHA256.HashData(block);
        CryptographicOperations.ZeroMemory(block);
        return result;
    }

    // ---------------------------------------------------------------- KDBX 4

    private static string ReadVersion4(byte[] file, byte[] composite)
    {
        var position = 12;
        var fields = ReadHeader(file, ref position, wideSizes: true);
        var header = file.AsSpan(0, position);

        if (!SHA256.HashData(header).AsSpan().SequenceEqual(file.AsSpan(position, 32)))
        {
            throw new KeePassImportException("The KeePass database header is damaged.");
        }

        var masterSeed = Required(fields, 4, 32);
        var transformed = TransformKey(composite, ReadVariantDictionary(Required(fields, 11)));
        var hmacBase = SHA512.HashData([.. masterSeed, .. transformed, 0x01]);
        var encryptionKey = SHA256.HashData([.. masterSeed, .. transformed]);
        try
        {
            var headerHmac = HMACSHA256.HashData(BlockKey(hmacBase, ulong.MaxValue), header);
            if (!CryptographicOperations.FixedTimeEquals(headerHmac, file.AsSpan(position + 32, 32)))
            {
                throw new KeePassImportException("The password or key file is wrong.");
            }

            position += 64;
            var payload = ReadHmacBlocks(file, position, hmacBase);
            var plain = Decrypt(Required(fields, 2, 16), encryptionKey, Required(fields, 7), payload);
            var inner = Compressed(fields) ? Inflate(plain) : plain;

            var offset = 0;
            var innerFields = new List<(byte Id, byte[] Data)>();
            while (true)
            {
                var id = inner[offset];
                var size = BinaryPrimitives.ReadInt32LittleEndian(inner.AsSpan(offset + 1));
                if (size < 0 || offset + 5 + size > inner.Length)
                {
                    throw new InvalidDataException();
                }

                var data = inner.AsSpan(offset + 5, size).ToArray();
                offset += 5 + size;
                if (id == 0)
                {
                    break;
                }

                innerFields.Add((id, data));
            }

            var streamId = BinaryPrimitives.ReadUInt32LittleEndian(innerFields.First(f => f.Id == 1).Data);
            var streamKey = innerFields.First(f => f.Id == 2).Data;
            var binaries = innerFields.Where(f => f.Id == 3 && f.Data.Length >= 1).Select(f => f.Data[1..]).ToList();
            return Unprotect(inner.AsSpan(offset), InnerStream(streamId, streamKey), binaries);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transformed);
            CryptographicOperations.ZeroMemory(hmacBase);
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static byte[] ReadHmacBlocks(byte[] file, int position, byte[] hmacBase)
    {
        using var output = new MemoryStream();
        var prefix = new byte[12];
        for (ulong index = 0; ; index++)
        {
            var hmac = file.AsSpan(position, 32);
            var size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(position + 32));
            if (size < 0 || position + 36L + size > file.Length)
            {
                throw new InvalidDataException();
            }

            var data = file.AsSpan(position + 36, size);
            BinaryPrimitives.WriteUInt64LittleEndian(prefix, index);
            BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(8), size);
            byte[] signed = [.. prefix, .. data];
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(BlockKey(hmacBase, index), signed), hmac))
            {
                throw new KeePassImportException("The KeePass database has been changed or damaged: a block does not match its check value.");
            }

            position += 36 + size;
            if (size == 0)
            {
                return output.ToArray();
            }

            output.Write(data);
        }
    }

    private static byte[] BlockKey(byte[] hmacBase, ulong index)
    {
        Span<byte> number = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(number, index);
        return SHA512.HashData([.. number, .. hmacBase]);
    }

    private static Dictionary<string, (byte Type, byte[] Value)> ReadVariantDictionary(byte[] data)
    {
        if (data.Length < 2 || data[1] != 0x01)
        {
            throw new KeePassImportException("The database's key settings use a newer format than MasterSafe reads.");
        }

        var result = new Dictionary<string, (byte, byte[])>(StringComparer.Ordinal);
        var position = 2;
        while (true)
        {
            var type = data[position++];
            if (type == 0)
            {
                return result;
            }

            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position));
            var name = Encoding.UTF8.GetString(data.AsSpan(position + 4, nameLength));
            position += 4 + nameLength;
            var valueLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position));
            result[name] = (type, data.AsSpan(position + 4, valueLength).ToArray());
            position += 4 + valueLength;
        }
    }

    // ---------------------------------------------------------------- KDBX 3.1

    private static string ReadVersion3(byte[] file, byte[] composite)
    {
        var position = 12;
        var fields = ReadHeader(file, ref position, wideSizes: false);
        var masterSeed = Required(fields, 4, 32);
        var rounds = BinaryPrimitives.ReadUInt64LittleEndian(Required(fields, 6, 8));
        var transformed = AesTransform(composite, Required(fields, 5, 32), rounds);
        var encryptionKey = SHA256.HashData([.. masterSeed, .. transformed]);
        try
        {
            var plain = Decrypt(Required(fields, 2, 16), encryptionKey, Required(fields, 7), file.AsSpan(position).ToArray());
            var startBytes = Required(fields, 9, 32);
            if (plain.Length < 32 || !CryptographicOperations.FixedTimeEquals(plain.AsSpan(0, 32), startBytes))
            {
                throw new KeePassImportException("The password or key file is wrong.");
            }

            var blocks = ReadHashedBlocks(plain, 32);
            var xml = Compressed(fields) ? Inflate(blocks) : blocks;
            var streamId = BinaryPrimitives.ReadUInt32LittleEndian(Required(fields, 10, 4));
            return Unprotect(xml, InnerStream(streamId, Required(fields, 8)), binaries: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transformed);
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static byte[] ReadHashedBlocks(byte[] data, int position)
    {
        using var output = new MemoryStream();
        while (true)
        {
            var hash = data.AsSpan(position + 4, 32);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position + 36));
            if (size < 0 || position + 40L + size > data.Length)
            {
                throw new InvalidDataException();
            }

            if (size == 0)
            {
                return output.ToArray();
            }

            var block = data.AsSpan(position + 40, size);
            if (!SHA256.HashData(block).AsSpan().SequenceEqual(hash))
            {
                throw new KeePassImportException("The KeePass database is damaged: a block does not match its check value.");
            }

            output.Write(block);
            position += 40 + size;
        }
    }

    // ---------------------------------------------------------------- shared

    private static Dictionary<byte, byte[]> ReadHeader(byte[] file, ref int position, bool wideSizes)
    {
        var fields = new Dictionary<byte, byte[]>();
        while (true)
        {
            var id = file[position];
            int size = wideSizes ? BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(position + 1)) : BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(position + 1));
            var start = position + (wideSizes ? 5 : 3);
            if (size < 0 || start + (long)size > file.Length)
            {
                throw new InvalidDataException();
            }

            fields[id] = file.AsSpan(start, size).ToArray();
            position = start + size;
            if (id == 0)
            {
                return fields;
            }
        }
    }

    private static byte[] Required(Dictionary<byte, byte[]> fields, byte id, int length = -1)
        => fields.TryGetValue(id, out var value) && (length < 0 || value.Length == length) ? value : throw new InvalidDataException();

    private static byte[] Get(Dictionary<string, (byte Type, byte[] Value)> dictionary, string name)
        => dictionary.TryGetValue(name, out var value) ? value.Value : throw new InvalidDataException();

    private static bool Compressed(Dictionary<byte, byte[]> fields)
        => fields.TryGetValue(3, out var flag) && flag.Length == 4 && BinaryPrimitives.ReadUInt32LittleEndian(flag) == 1;

    private static byte[] Decrypt(byte[] cipherId, byte[] key, byte[] iv, byte[] data)
    {
        if (cipherId.AsSpan().SequenceEqual(Aes256))
        {
            using var aes = Aes.Create();
            aes.Key = key;
            try
            {
                return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
            }
            catch (CryptographicException)
            {
                throw new KeePassImportException("The password or key file is wrong.");
            }
        }

        if (cipherId.AsSpan().SequenceEqual(ChaCha20Cipher))
        {
            var plain = (byte[])data.Clone();
            using var chacha = StreamCipher.ChaCha20(key, iv);
            chacha.Apply(plain);
            return plain;
        }

        throw new KeePassImportException("This database is encrypted with Twofish or another cipher MasterSafe does not support. In KeePass, change it to AES-256 or ChaCha20 under Database settings, Security.");
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxXmlBytes)
            {
                throw new KeePassImportException($"The database is larger than {MaxXmlBytes / (1024 * 1024)} MiB when unpacked, which is more than MasterSafe reads.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static StreamCipher InnerStream(uint id, byte[] key)
    {
        switch (id)
        {
            case 2:
                return StreamCipher.Salsa20(SHA256.HashData(key), Salsa20Nonce);
            case 3:
                var hash = SHA512.HashData(key);
                return StreamCipher.ChaCha20(hash.AsSpan(0, 32), hash.AsSpan(32, 12));
            default:
                throw new KeePassImportException("The database protects its values in a way MasterSafe does not support.");
        }
    }

    /// <summary>
    /// Decrypts the protected values in document order, as KeePass wrote them, and moves the attachments of a 4.x file
    /// into the XML, so the result reads like a KeePass XML export.
    /// </summary>
    private static string Unprotect(ReadOnlySpan<byte> xml, StreamCipher stream, List<byte[]>? binaries)
    {
        using (stream)
        {
            var document = XDocument.Load(XmlReader.Create(new MemoryStream(xml.ToArray()), SafeXml(MaxXmlBytes)));
            foreach (var value in document.Descendants("Value").Where(v => string.Equals((string?)v.Attribute("Protected"), "True", StringComparison.OrdinalIgnoreCase)))
            {
                var bytes = Convert.FromBase64String(value.Value);
                stream.Apply(bytes);
                value.Value = Encoding.UTF8.GetString(bytes);

                // Now readable, but still marked as a value KeePass hides, as KeePass's own XML export does.
                value.Attribute("Protected")!.Remove();
                value.SetAttributeValue("ProtectInMemory", "True");
                CryptographicOperations.ZeroMemory(bytes);
            }

            if (binaries is { Count: > 0 } && document.Root is { } root)
            {
                var meta = root.Element("Meta") ?? new XElement("Meta");
                if (meta.Parent is null)
                {
                    root.AddFirst(meta);
                }

                meta.Element("Binaries")?.Remove();
                meta.Add(new XElement("Binaries", binaries.Select((b, i) => new XElement("Binary", new XAttribute("ID", i), Convert.ToBase64String(b)))));
            }

            return document.ToString(SaveOptions.DisableFormatting);
        }
    }

    private static XmlReaderSettings SafeXml(long maxCharacters) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = maxCharacters,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };
}
