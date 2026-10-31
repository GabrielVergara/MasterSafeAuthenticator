// File:    AttachmentStorageTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public sealed class AttachmentStorageTests : IDisposable
{
    private const string Password = "a long test master password";

    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-attach-" + Guid.NewGuid().ToString("N"));

    public AttachmentStorageTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private VaultService NewVault()
        => new(new FileVaultRepository(Path.Combine(_folder, "vault.sauth")), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    private static (VaultContents Contents, PasswordAttachment A, PasswordAttachment B, byte[] BytesA, byte[] BytesB) Sample()
    {
        var bytesA = Encoding.UTF8.GetBytes("hello attachment");
        var bytesB = RandomNumberGenerator.GetBytes(300_000);
        var a = new PasswordAttachment { Name = "note.txt", Size = bytesA.Length };
        var b = new PasswordAttachment { Name = "key.bin", Size = bytesB.Length };
        var contents = new VaultContents
        {
            Passwords =
            [
                new PasswordEntry { Title = "With files", Password = "pw", Attachments = [a, b] },
                new PasswordEntry { Title = "Plain", Password = "pw2" },
            ],
            AttachmentData = new Dictionary<Guid, byte[]> { [a.Id] = bytesA, [b.Id] = bytesB },
        };
        return (contents, a, b, bytesA, bytesB);
    }

    [Fact]
    public void WithoutAttachments_TheFormatIsPlainJson_SoOlderVersionsStillOpenIt()
    {
        var bytes = VaultContentsCodec.Encode(new VaultContents { Passwords = [new PasswordEntry { Title = "A", Password = "p" }] });

        bytes[0].Should().Be((byte)'{');
        VaultContentsCodec.Decode(bytes).Passwords.Should().ContainSingle();
    }

    [Fact]
    public void WithAttachments_TheyRoundTrip_ByteForByte_AndAreNotInsideTheJson()
    {
        var (contents, a, b, bytesA, bytesB) = Sample();

        var encoded = VaultContentsCodec.Encode(contents);
        var decoded = VaultContentsCodec.Decode(encoded);

        Encoding.ASCII.GetString(encoded, 0, 4).Should().Be("MSV4");
        decoded.AttachmentData[a.Id].Should().Equal(bytesA);
        decoded.AttachmentData[b.Id].Should().Equal(bytesB);
        var entry = decoded.Passwords.Single(p => p.Title == "With files");
        entry.Attachments.Select(x => (x.Name, x.Size)).Should().Equal(("note.txt", (long)bytesA.Length), ("key.bin", (long)bytesB.Length));
        decoded.Passwords.Single(p => p.Title == "Plain").Attachments.Should().BeEmpty();
        encoded.Length.Should().BeLessThan(bytesA.Length + bytesB.Length + 2_000, "the bytes are stored raw, not as base64 text");
    }

    [Fact]
    public void FoldersAndIcons_RoundTrip_WithTheAttachments()
    {
        var (contents, _, _, _, _) = Sample();
        var withExtras = new VaultContents
        {
            Passwords = contents.Passwords,
            AttachmentData = contents.AttachmentData,
            Folders = ["Empty"],
            FolderIcons = new Dictionary<string, string> { ["Email"] = "mail" },
        };

        var decoded = VaultContentsCodec.Decode(VaultContentsCodec.Encode(withExtras));

        decoded.Folders.Should().Equal("Empty");
        decoded.FolderIcons.Should().Equal(new Dictionary<string, string> { ["Email"] = "mail" });
    }

    [Fact]
    public void FilesNoEntryRefersTo_AreNotSaved()
    {
        var (contents, _, _, _, _) = Sample();
        var orphan = Guid.NewGuid();
        var data = new Dictionary<Guid, byte[]>(contents.AttachmentData) { [orphan] = new byte[1000] };

        var decoded = VaultContentsCodec.Decode(VaultContentsCodec.Encode(new VaultContents { Passwords = contents.Passwords, AttachmentData = data }));

        decoded.AttachmentData.Should().HaveCount(2).And.NotContainKey(orphan);
    }

    [Fact]
    public void AnEntryWhoseFileIsMissing_CannotBeSaved()
    {
        var entry = new PasswordEntry { Title = "A", Password = "p", Attachments = [new PasswordAttachment { Name = "x", Size = 3 }] };

        var act = () => VaultContentsCodec.Encode(new VaultContents { Passwords = [entry] });

        act.Should().Throw<InvalidDataException>().WithMessage("*missing*");
    }

    [Fact]
    public void AFileOverTheLimit_CannotBeSaved()
    {
        var big = new byte[AttachmentLimits.MaxFileBytes + 1];
        var attachment = new PasswordAttachment { Name = "big", Size = big.Length };
        var contents = new VaultContents
        {
            Passwords = [new PasswordEntry { Title = "A", Password = "p", Attachments = [attachment] }],
            AttachmentData = new Dictionary<Guid, byte[]> { [attachment.Id] = big },
        };

        var act = () => VaultContentsCodec.Encode(contents);

        act.Should().Throw<InvalidDataException>().WithMessage("*larger*");
    }

    [Fact]
    public void TheTotalLimit_IsEnforced()
    {
        var attachments = Enumerable.Range(0, 11).Select(i => new PasswordAttachment { Name = "f" + i, Size = AttachmentLimits.MaxFileBytes }).ToList();
        var contents = new VaultContents
        {
            Passwords = [new PasswordEntry { Title = "A", Password = "p", Attachments = attachments }],
            AttachmentData = attachments.ToDictionary(a => a.Id, _ => new byte[AttachmentLimits.MaxFileBytes]),
        };

        var act = () => VaultContentsCodec.Encode(contents);

        act.Should().Throw<InvalidDataException>();
    }

    private static byte[] Container(string json, params (Guid Id, int DeclaredLength, byte[] Data)[] files)
    {
        using var stream = new MemoryStream();
        stream.Write("MSV4"u8);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, jsonBytes.Length);
        stream.Write(number);
        stream.Write(jsonBytes);
        BinaryPrimitives.WriteInt32LittleEndian(number, files.Length);
        stream.Write(number);
        foreach (var (id, declared, data) in files)
        {
            stream.Write(id.ToByteArray());
            BinaryPrimitives.WriteInt32LittleEndian(number, declared);
            stream.Write(number);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    [Fact]
    public void DamagedContainers_AreRefusedWithoutReservingHugeMemory()
    {
        var id = Guid.NewGuid();
        const string json = "{\"Passwords\":[]}";

        var hugeLength = Container(json, (id, int.MaxValue, [1, 2, 3]));
        var negativeLength = Container(json, (id, -5, [1, 2, 3]));
        var truncated = Container(json, (id, 3, [1, 2, 3]))[..^2];
        var tooShort = "MSV4"u8.ToArray();

        foreach (var bad in new[] { hugeLength, negativeLength, truncated, tooShort })
        {
            var act = () => VaultContentsCodec.Decode(bad);
            act.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void AClaimedJsonLengthBeyondTheData_IsRefused()
    {
        var data = new byte[12];
        "MSV4"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1_000_000);

        var act = () => VaultContentsCodec.Decode(data);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ADescriptionWhoseSizeDoesNotMatchTheFile_IsDropped()
    {
        var id = Guid.NewGuid();
        var json = $"{{\"Passwords\":[{{\"Title\":\"A\",\"Password\":\"p\",\"Attachments\":[{{\"Id\":\"{id}\",\"Name\":\"x\",\"Size\":99}}]}}]}}";

        var decoded = VaultContentsCodec.Decode(Container(json, (id, 3, [1, 2, 3])));

        decoded.Passwords.Single().Attachments.Should().BeEmpty();
        decoded.AttachmentData.Should().BeEmpty();
    }

    [Fact]
    public void HostileFileNamesInAVault_AreMadeSafe()
    {
        var id = Guid.NewGuid();
        var json = $"{{\"Passwords\":[{{\"Title\":\"A\",\"Password\":\"p\",\"Attachments\":[{{\"Id\":\"{id}\",\"Name\":\"..\\\\..\\\\Windows\\\\evil.exe\",\"Size\":3}}]}}]}}";

        var decoded = VaultContentsCodec.Decode(Container(json, (id, 3, [1, 2, 3])));

        decoded.Passwords.Single().Attachments.Single().Name.Should().Be("evil.exe");
    }

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("C:\\Users\\me\\secret.txt", "secret.txt")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("..", "attachment")]
    [InlineData("", "attachment")]
    [InlineData(null, "attachment")]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("name.", "name")]
    public void SafeName_GivesAPlainFileName(string? input, string expected)
        => AttachmentLimits.SafeName(input).Should().Be(expected);

    [Fact]
    public void SafeName_ShortensLongNames_KeepingTheExtension()
    {
        var name = AttachmentLimits.SafeName(new string('a', 400) + ".pdf");

        name.Should().HaveLength(AttachmentLimits.MaxNameLength).And.EndWith(".pdf");
    }

    [Fact]
    public async Task TheVaultFile_KeepsAttachments_AcrossSaveAndOpen_AndHidesThem()
    {
        var vault = NewVault();
        await vault.CreateAsync(Password);
        var (contents, a, b, bytesA, bytesB) = Sample();

        await vault.SaveContentsAsync(Password, contents);
        var opened = await vault.OpenContentsAsync(Password);

        opened.AttachmentData[a.Id].Should().Equal(bytesA);
        opened.AttachmentData[b.Id].Should().Equal(bytesB);
        opened.Passwords.Should().HaveCount(2);

        var raw = await File.ReadAllBytesAsync(Path.Combine(_folder, "vault.sauth"));
        Encoding.ASCII.GetString(raw).Should().NotContain("hello attachment").And.NotContain("note.txt");
    }

    [Fact]
    public async Task AVaultWithoutAttachments_IsStillPlainJsonInside_ForOlderVersions()
    {
        var vault = NewVault();
        await vault.CreateAsync(Password);

        await vault.SaveContentsAsync(Password, new VaultContents { Passwords = [new PasswordEntry { Title = "A", Password = "p" }] });
        var opened = await vault.OpenContentsAsync(Password);

        opened.Passwords.Should().ContainSingle();
        opened.AttachmentData.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoveryBackups_CarryTheAttachments_Too()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var service = new RecipientBackupService(new AesGcmEncryptionService(), PostQuantumProviderFactory.CreateHybrid());
        var keys = service.GenerateRecoveryKeyPair();
        var provider = new LocalFolderBackupProvider(Path.Combine(_folder, "backups"));
        var (contents, a, _, bytesA, _) = Sample();

        await service.ExportContentsAsync(contents, keys.PublicKey, provider);
        var restored = await service.ImportContentsAsync((await provider.ListAsync()).Single(), keys.PrivateKey, provider);

        restored.AttachmentData[a.Id].Should().Equal(bytesA);
        restored.Passwords.Single(p => p.Title == "With files").Attachments.Should().HaveCount(2);
    }
}
