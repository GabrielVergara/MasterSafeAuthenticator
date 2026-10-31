using FluentAssertions;
using SafeAuthenticator.Core;
using Xunit;

namespace SafeAuthenticator.Tests;

public class OtpAuthParserTests
{
    [Fact]
    public void Parse_TotpUri_ExtractsAllFields()
    {
        const string uri = "otpauth://totp/GitHub:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA256&digits=8&period=60";
        var account = OtpAuthParser.Parse(uri);

        account.Type.Should().Be(OtpType.Totp);
        account.Issuer.Should().Be("GitHub");
        account.Label.Should().Be("alice@example.com");
        account.Secret.Should().Be("JBSWY3DPEHPK3PXP");
        account.Algorithm.Should().Be(OtpAlgorithm.Sha256);
        account.Digits.Should().Be(8);
        account.Period.Should().Be(60);
    }

    [Fact]
    public void Parse_UsesIssuerFromLabelWhenQueryMissing()
    {
        const string uri = "otpauth://totp/ACME:bob?secret=JBSWY3DPEHPK3PXP";
        var account = OtpAuthParser.Parse(uri);
        account.Issuer.Should().Be("ACME");
        account.Label.Should().Be("bob");
    }

    [Fact]
    public void Parse_HotpUri_RequiresCounter()
    {
        const string uri = "otpauth://hotp/ACME:bob?secret=JBSWY3DPEHPK3PXP&counter=5";
        var account = OtpAuthParser.Parse(uri);
        account.Type.Should().Be(OtpType.Hotp);
        account.Counter.Should().Be(5);
    }

    [Fact]
    public void Parse_HotpWithoutCounter_Throws()
    {
        const string uri = "otpauth://hotp/ACME:bob?secret=JBSWY3DPEHPK3PXP";
        var act = () => OtpAuthParser.Parse(uri);
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("https://totp/x?secret=JBSWY3DPEHPK3PXP")]
    [InlineData("otpauth://unknown/x?secret=JBSWY3DPEHPK3PXP")]
    [InlineData("otpauth://totp/x")]
    [InlineData("otpauth://totp/x?secret=@@@invalid")]
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=9")]
    public void TryParse_RejectsMalformed(string uri)
    {
        OtpAuthParser.TryParse(uri, out var account).Should().BeFalse();
        account.Should().BeNull();
    }

    [Fact]
    public void Build_RoundTrips()
    {
        var account = new OtpAccount
        {
            Issuer = "GitHub",
            Label = "alice@example.com",
            Secret = "JBSWY3DPEHPK3PXP",
            Algorithm = OtpAlgorithm.Sha512,
            Digits = 8,
            Period = 30,
        };

        var uri = OtpAuthParser.Build(account);
        var parsed = OtpAuthParser.Parse(uri);

        parsed.Issuer.Should().Be(account.Issuer);
        parsed.Label.Should().Be(account.Label);
        parsed.Secret.Should().Be(account.Secret);
        parsed.Algorithm.Should().Be(account.Algorithm);
        parsed.Digits.Should().Be(account.Digits);
    }

    [Fact]
    public void Account_GenerateCode_Works()
    {
        var account = new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" };
        account.IsValid(out _).Should().BeTrue();
        account.GenerateCode(DateTimeOffset.UnixEpoch).Should().HaveLength(6);
    }
}
