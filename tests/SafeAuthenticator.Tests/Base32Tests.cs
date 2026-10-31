// File:    Base32Tests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Text;
using FluentAssertions;
using SafeAuthenticator.Core;
using Xunit;

namespace SafeAuthenticator.Tests;

public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Encode_MatchesRfc4648Vectors(string input, string expected)
    {
        var encoded = Base32.Encode(Encoding.ASCII.GetBytes(input));
        encoded.Should().Be(expected.TrimEnd('='));
    }

    [Theory]
    [InlineData("MY======", "f")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    [InlineData("mzxw6ytboi", "foobar")]
    public void Decode_MatchesRfc4648Vectors(string input, string expected)
    {
        var decoded = Base32.Decode(input);
        Encoding.ASCII.GetString(decoded).Should().Be(expected);
    }

    [Fact]
    public void Decode_IgnoresSpacesAndDashes()
    {
        var decoded = Base32.Decode("MZXW 6YTB-OI");
        Encoding.ASCII.GetString(decoded).Should().Be("foobar");
    }

    [Fact]
    public void IsValid_RejectsInvalidCharacters()
    {
        Base32.IsValid("@@@@").Should().BeFalse();
        Base32.IsValid(null).Should().BeFalse();
        Base32.IsValid("JBSWY3DPEHPK3PXP").Should().BeTrue();
    }

    [Fact]
    public void Decode_ThrowsOnInvalidCharacter()
    {
        var act = () => Base32.Decode("1111");
        act.Should().Throw<FormatException>();
    }
}
