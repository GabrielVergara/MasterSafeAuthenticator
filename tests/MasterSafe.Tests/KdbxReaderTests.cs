// File:    KdbxReaderTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text;
using FluentAssertions;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Core.Import;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>
/// Opens KeePass databases directly. The fixtures were made for these tests: 4.x by pykeepass with changed settings, 3.1 by a
/// small independent writer, each checked by pykeepass.
/// </summary>
public sealed class KdbxReaderTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "kdbx");

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Folder, name));

    [Fact]
    public void ChaCha20_MatchesRfc8439()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var data = Encoding.ASCII.GetBytes("Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
        using var cipher = StreamCipher.ChaCha20(key, Convert.FromHexString("000000000000004a00000000"), counter: 1);
        cipher.Apply(data);
        Convert.ToHexString(data).Should().StartWith("6E2E359A2568F98041BA0728DD0D6981").And.EndWith("B40B8EEDF2785E42874D");
    }

    [Fact]
    public void Salsa20_MatchesAnIndependentImplementation_AcrossBlocks()
    {
        var data = new byte[130];
        using var cipher = StreamCipher.Salsa20(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(), Convert.FromHexString("E830094B97205D2A"));
        cipher.Apply(data.AsSpan(0, 50));
        cipher.Apply(data.AsSpan(50));
        Convert.ToHexString(data).Should().StartWith("58538C4E383A24951F6F5A9C814A5FA7").And.EndWith("63DFD3");
    }

    [Theory]
    [InlineData("kdbx4-aes-argon2d.kdbx", "fixture-kdbx-4", false)]
    [InlineData("kdbx4-chacha-argon2id-keyfile.kdbx", "fixture-kdbx-4", true)]
    [InlineData("kdbx3-aes-aeskdf.kdbx", "fixture-kdbx-3", false)]
    public void ADatabase_OpensWithItsKey_AndImportsLikeAnExport(string file, string password, bool keyFile)
    {
        var xml = KdbxReader.ReadXml(Fixture(file), password, keyFile ? Fixture("fixture.keyx") : null);
        var result = KeePassImporter.ParseXml(xml);

        var gmail = result.Passwords.Single(p => p.Title == "Gmail");
        gmail.Password.Should().Be("pa55-Wörd!", "protected values are decrypted in order");
        gmail.UserName.Should().Be("alice@example.org");
        gmail.Folder.Should().Be("Mail");
        result.Passwords.Single(p => p.Title == "Bank").Password.Should().Be("second-secret");

        if (file.StartsWith("kdbx4", StringComparison.Ordinal))
        {
            gmail.Fields.Should().ContainSingle(f => f.Name == "PIN" && f.Value == "4321" && f.IsSecret, "a field KeePass protected stays hidden");
            result.Accounts.Should().ContainSingle(a => a.Secret == "JBSWY3DPEHPK3PXP");
            gmail.Attachments.Should().ContainSingle(a => a.Name == "readme.txt");
            result.Attachments.Values.Should().ContainSingle(b => Encoding.ASCII.GetString(b) == "attached file content");
        }
        else
        {
            result.SkippedHistory.Should().Be(1, "the old version's protected password was decrypted in its place, keeping the order");
        }
    }

    [Theory]
    [InlineData("kdbx4-aes-argon2d.kdbx")]
    [InlineData("kdbx3-aes-aeskdf.kdbx")]
    public void AWrongPassword_IsSaidPlainly(string file)
    {
        var open = () => KdbxReader.ReadXml(Fixture(file), "not-the-password", null);
        open.Should().Throw<KeePassImportException>().WithMessage("*password or key file is wrong*");
    }

    [Fact]
    public void AMissingKeyFile_IsAWrongKey()
    {
        var open = () => KdbxReader.ReadXml(Fixture("kdbx4-chacha-argon2id-keyfile.kdbx"), "fixture-kdbx-4", null);
        open.Should().Throw<KeePassImportException>().WithMessage("*password or key file is wrong*");
    }

    [Fact]
    public void AChangedByte_InTheBody_IsCaught()
    {
        var file = Fixture("kdbx4-aes-argon2d.kdbx");
        file[^40] ^= 1;
        var open = () => KdbxReader.ReadXml(file, "fixture-kdbx-4", null);
        open.Should().Throw<KeePassImportException>().WithMessage("*changed or damaged*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(200)]
    public void ACutFile_FailsCleanly(int keep)
    {
        var file = Fixture("kdbx3-aes-aeskdf.kdbx").Take(Math.Max(keep, 12)).ToArray();
        var open = () => KdbxReader.ReadXml(file, "fixture-kdbx-3", null);
        open.Should().Throw<KeePassImportException>();
    }

    [Fact]
    public void ADatabase_AskingForTooMuchMemory_IsRefusedBeforeAnyWork()
    {
        var file = Fixture("kdbx4-aes-argon2d.kdbx");
        var at = IndexOf(file, "M"u8.ToArray(), after: 12) + 1 + 4;
        BitConverter.GetBytes(64UL * 1024 * 1024 * 1024).CopyTo(file, at);

        // A changed header fails its hash first, so the hash is made to match: only the limit can stop it now.
        var end = 12;
        while (true)
        {
            var id = file[end];
            end += 5 + BitConverter.ToInt32(file, end + 1);
            if (id == 0)
            {
                break;
            }
        }

        System.Security.Cryptography.SHA256.HashData(file.AsSpan(0, end)).CopyTo(file, end);
        var open = () => KdbxReader.ReadXml(file, "fixture-kdbx-4", null);
        open.Should().Throw<KeePassImportException>().WithMessage("*more memory or time*");
    }

    [Fact]
    public void WhatMasterSafeExports_ItCanReadBack()
    {
        var contents = new VaultContents { Passwords = [new PasswordEntry { Title = "Round trip", UserName = "u", Password = "p@ss", Folder = "Work" }] };
        var fast = new Argon2Parameters { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1 };

        var result = KeePassImporter.ParseXml(KdbxReader.ReadXml(KdbxWriter.Write(contents, "export-password", fast), "export-password", null));

        result.Passwords.Should().ContainSingle(p => p.Title == "Round trip" && p.Password == "p@ss" && p.Folder == "Work");
    }

    [Fact]
    public void KeyFiles_InEveryKeePassForm_GiveTheSameKey()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        KdbxReader.KeyFileHash(Fixture("fixture.keyx")).Should().Equal(key, "XML 2.0 with its check value");
        KdbxReader.KeyFileHash(key).Should().Equal(key, "32 raw bytes");
        KdbxReader.KeyFileHash(Encoding.ASCII.GetBytes(Convert.ToHexString(key))).Should().Equal(key, "64 hex digits");
        KdbxReader.KeyFileHash(Encoding.UTF8.GetBytes($"<KeyFile><Meta><Version>1.00</Version></Meta><Key><Data>{Convert.ToBase64String(key)}</Data></Key></KeyFile>"))
            .Should().Equal(key, "XML 1.0");
        KdbxReader.KeyFileHash("any file"u8.ToArray()).Should().Equal(System.Security.Cryptography.SHA256.HashData("any file"u8.ToArray()));
    }

    private static int IndexOf(byte[] data, byte[] name, int after)
    {
        // A KDF parameter is stored as: type, name length (4 bytes), name, value length (4 bytes), value.
        for (var i = after; i < data.Length - 5; i++)
        {
            if (data[i] == 1 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 0 && data[i + 4] == name[0])
            {
                return i + 4;
            }
        }

        throw new InvalidOperationException("Parameter not found.");
    }
}
