// File:    QrCodeTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using FluentAssertions;
using SafeAuthenticator.Core;
using SafeAuthenticator.QR;
using Xunit;

namespace SafeAuthenticator.Tests;

public class QrCodeTests
{
    private const string Uri = "otpauth://totp/GitHub:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA1&digits=6&period=30";

    [Fact]
    public void GenerateThenDecode_RoundTrips()
    {
        var png = new QrCodeGenerator().GeneratePng(Uri, 300);
        png.Should().NotBeEmpty();
        using var stream = new MemoryStream(png);
        var account = new QrCodeImporter().ImportFromStream(stream);
        account.Issuer.Should().Be("GitHub");
        account.Label.Should().Be("alice@example.com");
        account.Secret.Should().Be("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Import_NonOtpauthQr_Throws()
    {
        var png = new QrCodeGenerator().GeneratePng("https://example.com", 300);
        using var stream = new MemoryStream(png);
        var act = () => new QrCodeImporter().ImportFromStream(stream);
        act.Should().Throw<QrImportException>();
    }

    [Fact]
    public void DecodeFromFile_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qr-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, new QrCodeGenerator().GeneratePng(Uri, 300));
            new QrCodeImporter().TryImportFromFile(path, out var account).Should().BeTrue();
            account!.Type.Should().Be(OtpType.Totp);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnsupportedExtension_IsRejected()
    {
        QrImageFormats.IsSupported("x.gif").Should().BeFalse();
        QrImageFormats.IsSupported("x.PNG").Should().BeTrue();
    }
}
