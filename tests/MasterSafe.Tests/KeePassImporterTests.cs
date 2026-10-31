// File:    KeePassImporterTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.Core;
using MasterSafe.Core.Import;
using Xunit;

namespace MasterSafe.Tests;

public sealed class KeePassImporterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-keepass-" + Guid.NewGuid().ToString("N"));

    public KeePassImporterTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private const string Xml = """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <KeePassFile>
          <Meta><RecycleBinUUID>BIN=</RecycleBinUUID></Meta>
          <Root>
            <Group>
              <UUID>ROOT=</UUID><Name>Database</Name>
              <Entry>
                <String><Key>Title</Key><Value>Top level</Value></String>
                <String><Key>UserName</Key><Value>me</Value></String>
                <String><Key>Password</Key><Value ProtectInMemory="True">p&amp;ss &lt;1&gt;</Value></String>
                <Times><LastModificationTime>2024-05-06T07:08:09Z</LastModificationTime></Times>
              </Entry>
              <Group>
                <UUID>MAIL=</UUID><Name>Email</Name>
                <Entry>
                  <String><Key>Title</Key><Value>Gmail</Value></String>
                  <String><Key>UserName</Key><Value>alice@example.com</Value></String>
                  <String><Key>Password</Key><Value>secret</Value></String>
                  <String><Key>URL</Key><Value>https://mail.google.com</Value></String>
                  <String><Key>Notes</Key><Value>line 1&#10;line 2</Value></String>
                  <String><Key>otp</Key><Value>otpauth://totp/Google:alice@example.com?secret=JBSWY3DPEHPK3PXP&amp;issuer=Google</Value></String>
                  <Binary><Key>scan.pdf</Key><Value Ref="0" /></Binary>
                  <History>
                    <Entry><String><Key>Title</Key><Value>Gmail</Value></String></Entry>
                    <Entry><String><Key>Title</Key><Value>Gmail</Value></String></Entry>
                  </History>
                </Entry>
                <Group>
                  <UUID>WORK=</UUID><Name>Work</Name>
                  <Entry>
                    <String><Key>Title</Key><Value>VPN</Value></String>
                    <String><Key>Password</Key><Value>vpn-pass</Value></String>
                    <String><Key>TimeOtp-Secret-Base32</Key><Value>GEZDGNBVGY3TQOJQ</Value></String>
                    <String><Key>TimeOtp-Period</Key><Value>60</Value></String>
                    <String><Key>TimeOtp-Length</Key><Value>8</Value></String>
                    <String><Key>TimeOtp-Algorithm</Key><Value>HMAC-SHA-256</Value></String>
                  </Entry>
                </Group>
              </Group>
              <Group>
                <UUID>BIN=</UUID><Name>Recycle Bin</Name>
                <Entry>
                  <String><Key>Title</Key><Value>Deleted</Value></String>
                  <String><Key>Password</Key><Value>gone</Value></String>
                </Entry>
              </Group>
              <Entry>
                <String><Key>Title</Key><Value>No password</Value></String>
              </Entry>
            </Group>
          </Root>
        </KeePassFile>
        """;

    [Fact]
    public void Xml_ReadsPasswords_GroupsAndSkipsWhatItShould()
    {
        var result = KeePassImporter.ParseXml(Xml);

        result.Passwords.Select(p => p.Title).Should().BeEquivalentTo("Top level", "Gmail", "VPN");
        result.TotalEntries.Should().Be(5);
        result.SkippedRecycleBin.Should().Be(1);
        result.SkippedHistory.Should().Be(2);
        result.SkippedAttachments.Should().Be(1);
        result.SkippedInvalid.Should().Be(1);

        var top = result.Passwords.Single(p => p.Title == "Top level");
        top.Password.Should().Be("p&ss <1>");
        top.Folder.Should().BeEmpty("the top group is the database itself");
        top.ModifiedUtc.Should().Be(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero));

        var gmail = result.Passwords.Single(p => p.Title == "Gmail");
        gmail.UserName.Should().Be("alice@example.com");
        gmail.Url.Should().Be("https://mail.google.com");
        gmail.Notes.Should().Contain("line 2");
        gmail.Folder.Should().Be("Email");

        result.Passwords.Single(p => p.Title == "VPN").Folder.Should().Be("Email/Work");
    }

    [Fact]
    public void Xml_OneTimePasswords_BecomeAuthenticatorAccounts()
    {
        var result = KeePassImporter.ParseXml(Xml);

        result.Accounts.Should().HaveCount(2);
        var google = result.Accounts.Single(a => a.Issuer == "Google");
        google.Label.Should().Be("alice@example.com");
        google.Secret.Should().Be("JBSWY3DPEHPK3PXP");

        var vpn = result.Accounts.Single(a => a.Issuer == "VPN");
        vpn.Secret.Should().Be("GEZDGNBVGY3TQOJQ");
        vpn.Period.Should().Be(60);
        vpn.Digits.Should().Be(8);
        vpn.Algorithm.Should().Be(OtpAlgorithm.Sha256);
    }

    [Fact]
    public void Xml_WithADoctype_IsRefused_SoEntitiesCannotBeUsed()
    {
        const string hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE KeePassFile [<!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">]>
            <KeePassFile><Root><Group><Name>x</Name><Entry>
              <String><Key>Title</Key><Value>&xxe;</Value></String>
              <String><Key>Password</Key><Value>p</Value></String>
            </Entry></Group></Root></KeePassFile>
            """;

        var act = () => KeePassImporter.ParseXml(hostile);

        act.Should().Throw<KeePassImportException>();
    }

    [Fact]
    public void Xml_ValuesProtectedInsideTheFile_AreReportedNotImportedAsGarbage()
    {
        const string xml = """
            <KeePassFile><Root><Group><Name>x</Name><Entry>
              <String><Key>Title</Key><Value>A</Value></String>
              <String><Key>Password</Key><Value Protected="True">c2VjcmV0</Value></String>
            </Entry></Group></Root></KeePassFile>
            """;

        var result = KeePassImporter.ParseXml(xml);

        result.Passwords.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("protected");
    }

    [Theory]
    [InlineData("<html><body/></html>")]
    [InlineData("<KeePassFile><Root/></KeePassFile>")]
    [InlineData("this is not xml <")]
    public void Xml_ThatIsNotAnExport_IsRefused(string text)
    {
        var act = () => KeePassImporter.ParseXml(text);

        act.Should().Throw<KeePassImportException>();
    }

    [Fact]
    public void Xml_NestedTooDeeply_IsRefused()
    {
        var open = string.Concat(Enumerable.Repeat("<Group><Name>g</Name>", 200));
        var close = string.Concat(Enumerable.Repeat("</Group>", 200));

        var act = () => KeePassImporter.ParseXml($"<KeePassFile><Root>{open}{close}</Root></KeePassFile>");

        act.Should().Throw<KeePassImportException>().WithMessage("*nested*");
    }

    private const string Csv =
        "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\",\"Icon\",\"Last Modified\",\"Created\"\r\n"
        + "\"Root\",\"Top\",\"me\",\"pw1\",\"\",\"\",\"\",\"0\",\"2024-01-01T00:00:00Z\",\"2024-01-01T00:00:00Z\"\r\n"
        + "\"Root/Email\",\"Gmail\",\"alice@example.com\",\"has \"\"quotes\"\", and comma\",\"https://mail.google.com\",\"two\r\nlines\","
        + "\"otpauth://totp/Google:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Google\",\"0\",\"\",\"\"\r\n"
        + "\"Root/Email\",\"Bare\",\"bob\",\"pw3\",\"\",\"\",\"jbsw y3dp ehpk 3pxp\",\"0\",\"\",\"\"\r\n"
        + "\"Root\",\"\",\"\",\"\",\"\",\"\",\"\",\"0\",\"\",\"\"\r\n";

    [Fact]
    public void Csv_ReadsQuotedFieldsGroupsAndOneTimePasswords()
    {
        var result = KeePassImporter.ParseCsv(Csv);

        result.Passwords.Select(p => p.Title).Should().BeEquivalentTo("Top", "Gmail", "Bare");
        result.SkippedInvalid.Should().Be(1);

        var gmail = result.Passwords.Single(p => p.Title == "Gmail");
        gmail.Password.Should().Be("has \"quotes\", and comma");
        gmail.Notes.Should().Be("two\nlines");
        gmail.Folder.Should().Be("Email");
        result.Passwords.Single(p => p.Title == "Top").Folder.Should().BeEmpty();

        result.Accounts.Should().HaveCount(2);
        result.Accounts.Single(a => a.Issuer == "Google").Secret.Should().Be("JBSWY3DPEHPK3PXP");
        result.Accounts.Single(a => a.Issuer == "Bare").Secret.Should().Be("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Csv_FromKeePass2_UsesItsOwnColumnNames()
    {
        const string csv = "\"Account\",\"Login Name\",\"Password\",\"Web Site\",\"Comments\"\n\"Bank\",\"jo\",\"pw\",\"https://bank.example\",\"note\"\n";

        var result = KeePassImporter.ParseCsv(csv);

        var bank = result.Passwords.Should().ContainSingle().Subject;
        (bank.Title, bank.UserName, bank.Url, bank.Notes).Should().Be(("Bank", "jo", "https://bank.example", "note"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Name,Value\nA,B\n")]
    public void Csv_WithoutTitleAndPasswordColumns_IsRefused(string text)
    {
        var act = () => KeePassImporter.ParseCsv(text);

        act.Should().Throw<KeePassImportException>();
    }

    [Fact]
    public void Csv_WithAnUnterminatedQuote_DoesNotHangOrCrash()
    {
        var result = KeePassImporter.ParseCsv("Title,Password\n\"A,pw\nB,pw2\n");

        result.TotalEntries.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void ReadFile_ChoosesTheFormatFromTheContent()
    {
        var xml = Path.Combine(_folder, "export.xml");
        var csv = Path.Combine(_folder, "export.csv");
        File.WriteAllText(xml, Xml);
        File.WriteAllText(csv, Csv);

        KeePassImporter.ReadFile(xml).Passwords.Should().HaveCount(3);
        KeePassImporter.ReadFile(csv).Passwords.Should().HaveCount(3);
    }

    [Fact]
    public void ReadFile_WithABom_StillWorks()
    {
        var path = Path.Combine(_folder, "bom.csv");
        File.WriteAllText(path, "Title,Password\nA,pw\n", new System.Text.UTF8Encoding(true));

        KeePassImporter.ReadFile(path).Passwords.Should().ContainSingle();
    }

    [Fact]
    public void ReadFile_OfAKdbxDatabase_ExplainsHowToExport()
    {
        var path = Path.Combine(_folder, "db.kdbx");
        File.WriteAllBytes(path, [0x03, 0xD9, 0xA2, 0x9A, 0x67, 0xFB, 0x4B, 0xB5, 0, 0, 4, 0]);

        var act = () => KeePassImporter.ReadFile(path);

        act.Should().Throw<KeePassImportException>().WithMessage("*Export*");
    }

    [Fact]
    public void ReadFile_MissingOrHuge_IsRefused()
    {
        var missing = () => KeePassImporter.ReadFile(Path.Combine(_folder, "nope.csv"));
        missing.Should().Throw<KeePassImportException>();

        var huge = Path.Combine(_folder, "huge.csv");
        using (var stream = File.Create(huge))
        {
            stream.SetLength(KeePassImporter.MaxFileBytes + 1);
        }

        var tooBig = () => KeePassImporter.ReadFile(huge);
        tooBig.Should().Throw<KeePassImportException>().WithMessage("*larger*");
    }

    [Fact]
    public void TooManyEntries_AreRefused()
    {
        var rows = string.Concat(Enumerable.Range(0, KeePassImporter.MaxEntries + 1).Select(i => $"T{i},p\n"));

        var act = () => KeePassImporter.ParseCsv("Title,Password\n" + rows);

        act.Should().Throw<KeePassImportException>().WithMessage("*more than*");
    }
}
