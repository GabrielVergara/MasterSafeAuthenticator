// File:    KdbxWriter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.Backup;

/// <summary>
/// Writes a KeePass database (KDBX 4.0, Argon2id and AES-256), so nobody is locked in to MasterSafe. The format is public
/// and this is written from its description, with no GPL code. Authenticator accounts become entries in a group of their own,
/// with their otpauth link in the <c>otp</c> field that KeePassXC understands.
/// <para>
/// Everything that is inside the file is protected by the database password. The inner stream that KeePass uses to hide values
/// in memory adds nothing on disk, so values are stored plainly inside the encrypted block.
/// </para>
/// </summary>
public static class KdbxWriter
{
    private const uint SignatureOne = 0x9AA2D903;
    private const uint SignatureTwo = 0xB54BFB67;
    private const uint FileVersion = 0x00040000;
    private const int BlockSize = 1024 * 1024;

    private static readonly byte[] Aes256Cipher = Convert.FromHexString("31C1F2E6BF714350BE5805216AFC5AFF");
    private static readonly byte[] Argon2idKdf = Convert.FromHexString("9E298B1956DB4773B23DFC3EC6F0A1E6");

    /// <summary>Writes <paramref name="contents"/> as a database protected by <paramref name="password"/>.</summary>
    public static byte[] Write(VaultContents contents, string password, Argon2Parameters? kdf = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var parameters = kdf ?? Argon2Parameters.Default;
        var masterSeed = RandomNumberGenerator.GetBytes(32);
        var kdfSalt = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);

        var compositeKey = CompositeKey(password);
        var transformed = Argon2Raw(compositeKey, kdfSalt, parameters);

        var header = BuildHeader(masterSeed, iv, kdfSalt, parameters);
        var encryptionKey = SHA256.HashData([.. masterSeed, .. transformed]);
        var hmacBase = SHA512.HashData([.. masterSeed, .. transformed, 0x01]);

        using var output = new MemoryStream();
        output.Write(header);
        output.Write(SHA256.HashData(header));
        output.Write(HMACSHA256.HashData(BlockKey(hmacBase, ulong.MaxValue), header));

        var payload = EncryptPayload(BuildInner(contents, out var binaries), binaries, encryptionKey, iv);
        WriteBlocks(output, payload, hmacBase);

        CryptographicOperations.ZeroMemory(compositeKey);
        CryptographicOperations.ZeroMemory(transformed);
        CryptographicOperations.ZeroMemory(encryptionKey);
        CryptographicOperations.ZeroMemory(hmacBase);
        return output.ToArray();
    }

    private static byte[] CompositeKey(string password) => SHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    private static byte[] Argon2Raw(byte[] key, byte[] salt, Argon2Parameters p)
    {
        using var argon2 = new Konscious.Security.Cryptography.Argon2id(key)
        {
            Salt = salt,
            MemorySize = p.MemorySizeKib,
            Iterations = p.Iterations,
            DegreeOfParallelism = p.DegreeOfParallelism,
        };
        return argon2.GetBytes(32);
    }

    private static byte[] BlockKey(byte[] hmacBase, ulong index)
    {
        Span<byte> number = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(number, index);
        return SHA512.HashData([.. number, .. hmacBase]);
    }

    private static byte[] BuildHeader(byte[] masterSeed, byte[] iv, byte[] kdfSalt, Argon2Parameters p)
    {
        using var header = new MemoryStream();
        WriteUInt32(header, SignatureOne);
        WriteUInt32(header, SignatureTwo);
        WriteUInt32(header, FileVersion);
        WriteField(header, 2, Aes256Cipher);
        WriteField(header, 3, [1, 0, 0, 0]);
        WriteField(header, 4, masterSeed);
        WriteField(header, 7, iv);
        WriteField(header, 11, KdfParameters(kdfSalt, p));
        WriteField(header, 0, "\r\n\r\n"u8.ToArray());
        return header.ToArray();
    }

    private static byte[] KdfParameters(byte[] salt, Argon2Parameters p)
    {
        using var dictionary = new MemoryStream();
        dictionary.Write([0x00, 0x01]);
        WriteVariant(dictionary, 0x42, "$UUID", Argon2idKdf);
        WriteVariant(dictionary, 0x42, "S", salt);
        WriteVariant(dictionary, 0x04, "P", UInt32Bytes((uint)p.DegreeOfParallelism));
        WriteVariant(dictionary, 0x05, "M", UInt64Bytes((ulong)p.MemorySizeKib * 1024));
        WriteVariant(dictionary, 0x05, "I", UInt64Bytes((ulong)p.Iterations));
        WriteVariant(dictionary, 0x04, "V", UInt32Bytes(0x13));
        dictionary.WriteByte(0);
        return dictionary.ToArray();
    }

    private static void WriteVariant(Stream stream, byte type, string name, byte[] value)
    {
        stream.WriteByte(type);
        WriteUInt32(stream, (uint)name.Length);
        stream.Write(Encoding.ASCII.GetBytes(name));
        WriteUInt32(stream, (uint)value.Length);
        stream.Write(value);
    }

    private static byte[] EncryptPayload(byte[] xml, List<byte[]> binaries, byte[] key, byte[] iv)
    {
        using var inner = new MemoryStream();
        WriteField(inner, 1, UInt32Bytes(3));
        WriteField(inner, 2, RandomNumberGenerator.GetBytes(64));
        foreach (var binary in binaries)
        {
            WriteField(inner, 3, [0, .. binary]);
        }

        WriteField(inner, 0, []);
        inner.Write(xml);

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(inner.GetBuffer(), 0, (int)inner.Length);
        }

        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(compressed.GetBuffer().AsSpan(0, (int)compressed.Length), iv);
    }

    private static void WriteBlocks(Stream output, byte[] payload, byte[] hmacBase)
    {
        ulong index = 0;
        for (var offset = 0; ; offset += BlockSize)
        {
            var size = Math.Min(BlockSize, payload.Length - offset);
            var block = size > 0 ? payload.AsSpan(offset, size).ToArray() : [];
            var signed = new byte[12 + block.Length];
            BinaryPrimitives.WriteUInt64LittleEndian(signed, index);
            BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(8), block.Length);
            block.CopyTo(signed, 12);

            output.Write(HMACSHA256.HashData(BlockKey(hmacBase, index), signed));
            output.Write(signed.AsSpan(8));
            index++;
            if (block.Length == 0)
            {
                return;
            }
        }
    }

    private static byte[] BuildInner(VaultContents contents, out List<byte[]> binaries)
    {
        var pool = new List<byte[]>();
        var active = contents.Passwords.Where(p => !p.IsDeleted).ToList();

        using var buffer = new MemoryStream();
        using (var xml = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CheckCharacters = false }))
        {
            xml.WriteStartDocument(true);
            xml.WriteStartElement("KeePassFile");
            xml.WriteStartElement("Meta");
            xml.WriteElementString("Generator", "MasterSafe Authenticator");
            xml.WriteElementString("DatabaseName", "MasterSafe");
            xml.WriteEndElement();
            xml.WriteStartElement("Root");
            xml.WriteStartElement("Group");
            WriteUuid(xml, Guid.NewGuid());
            xml.WriteElementString("Name", "MasterSafe");

            WriteFolder(xml, BuildTree(active), contents, pool);

            if (contents.Accounts.Count > 0)
            {
                xml.WriteStartElement("Group");
                WriteUuid(xml, Guid.NewGuid());
                xml.WriteElementString("Name", "Authenticator");
                foreach (var account in contents.Accounts)
                {
                    WriteAccount(xml, account);
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        binaries = pool;
        return buffer.ToArray();
    }

    private sealed class FolderNode
    {
        public List<PasswordEntry> Entries { get; } = [];

        public SortedDictionary<string, FolderNode> Children { get; } = new(StringComparer.Ordinal);
    }

    private static FolderNode BuildTree(IEnumerable<PasswordEntry> entries)
    {
        var root = new FolderNode();
        foreach (var entry in entries)
        {
            var node = root;
            foreach (var part in entry.Folder.Split(FolderPath.Separator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!node.Children.TryGetValue(part, out var child))
                {
                    child = new FolderNode();
                    node.Children[part] = child;
                }

                node = child;
            }

            node.Entries.Add(entry);
        }

        return root;
    }

    private static void WriteFolder(XmlWriter xml, FolderNode node, VaultContents contents, List<byte[]> pool)
    {
        foreach (var entry in node.Entries)
        {
            WriteEntry(xml, entry, contents, pool);
        }

        foreach (var (name, child) in node.Children)
        {
            xml.WriteStartElement("Group");
            WriteUuid(xml, Guid.NewGuid());
            xml.WriteElementString("Name", Clean(name));
            WriteFolder(xml, child, contents, pool);
            xml.WriteEndElement();
        }
    }

    private static void WriteEntry(XmlWriter xml, PasswordEntry entry, VaultContents contents, List<byte[]> pool)
    {
        xml.WriteStartElement("Entry");
        WriteUuid(xml, entry.Id);
        WriteTimes(xml, entry.ModifiedUtc, entry.ExpiresUtc);
        WriteString(xml, "Title", entry.Title);
        WriteString(xml, "UserName", entry.UserName);
        WriteString(xml, "Password", entry.Password);
        WriteString(xml, "URL", entry.Url);
        WriteString(xml, "Notes", entry.Notes);

        var used = new HashSet<string>(["Title", "UserName", "Password", "URL", "Notes"], StringComparer.OrdinalIgnoreCase);
        foreach (var field in entry.Fields)
        {
            // KeePass needs each field name once on an entry.
            var name = field.Name.Length == 0 ? "Field" : field.Name;
            for (var n = 2; !used.Add(name); n++)
            {
                name = $"{field.Name} ({n})";
            }

            WriteString(xml, name, field.Value);
        }

        if (entry.Tags.Count > 0)
        {
            xml.WriteElementString("Tags", Clean(string.Join(';', entry.Tags)));
        }

        foreach (var attachment in entry.Attachments)
        {
            if (contents.AttachmentData.TryGetValue(attachment.Id, out var bytes))
            {
                xml.WriteStartElement("Binary");
                xml.WriteElementString("Key", Clean(attachment.Name));
                xml.WriteStartElement("Value");
                xml.WriteAttributeString("Ref", pool.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                xml.WriteEndElement();
                xml.WriteEndElement();
                pool.Add(bytes);
            }
        }

        xml.WriteEndElement();
    }

    private static void WriteAccount(XmlWriter xml, OtpAccount account)
    {
        xml.WriteStartElement("Entry");
        WriteUuid(xml, account.Id);
        WriteTimes(xml, account.ModifiedUtc ?? DateTimeOffset.UtcNow, null);
        WriteString(xml, "Title", account.Issuer.Length > 0 ? account.Issuer : account.Label);
        WriteString(xml, "UserName", account.Label);
        WriteString(xml, "Password", string.Empty);
        WriteString(xml, "otp", OtpAuthParser.Build(account));
        xml.WriteEndElement();
    }

    private static void WriteTimes(XmlWriter xml, DateTimeOffset modified, DateTimeOffset? expires)
    {
        xml.WriteStartElement("Times");
        xml.WriteElementString("LastModificationTime", KdbxTime(modified));
        xml.WriteElementString("CreationTime", KdbxTime(modified));
        xml.WriteElementString("LastAccessTime", KdbxTime(modified));
        xml.WriteElementString("Expires", expires is null ? "False" : "True");
        if (expires is { } date)
        {
            xml.WriteElementString("ExpiryTime", KdbxTime(date));
        }

        xml.WriteEndElement();
    }

    // Seconds since year 1, as 8 bytes in Base64: the time format of KDBX 4.
    private static string KdbxTime(DateTimeOffset time)
        => Convert.ToBase64String(UInt64Bytes((ulong)((time.UtcDateTime - DateTime.MinValue).Ticks / TimeSpan.TicksPerSecond)));

    private static void WriteString(XmlWriter xml, string key, string value)
    {
        xml.WriteStartElement("String");
        xml.WriteElementString("Key", Clean(key));
        xml.WriteElementString("Value", Clean(value));
        xml.WriteEndElement();
    }

    private static void WriteUuid(XmlWriter xml, Guid id) => xml.WriteElementString("UUID", Convert.ToBase64String(id.ToByteArray()));

    // A character that XML cannot carry would make the whole database unreadable, so it is left out.
    private static string Clean(string text)
        => text.All(XmlConvert.IsXmlChar) ? text : new string(text.Where(XmlConvert.IsXmlChar).ToArray());

    private static void WriteField(Stream stream, byte id, byte[] data)
    {
        stream.WriteByte(id);
        WriteUInt32(stream, (uint)data.Length);
        stream.Write(data);
    }

    private static void WriteUInt32(Stream stream, uint value) => stream.Write(UInt32Bytes(value));

    private static byte[] UInt32Bytes(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] UInt64Bytes(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }
}
