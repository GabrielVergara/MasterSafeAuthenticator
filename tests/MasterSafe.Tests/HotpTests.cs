// File:    HotpTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text;
using FluentAssertions;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public class HotpTests
{
    // RFC 4226 Appendix D test secret: ASCII "12345678901234567890".
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Generate_MatchesRfc4226Vectors(long counter, string expected)
    {
        var code = HotpGenerator.Generate(Secret, counter);
        code.Should().Be(expected);
    }

    [Fact]
    public void Generate_RejectsEmptySecret()
    {
        var act = () => HotpGenerator.Generate([], 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Generate_RejectsInvalidDigits()
    {
        var act = () => HotpGenerator.Generate(Secret, 0, digits: 9);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Generate_SupportsEightDigits()
    {
        var code = HotpGenerator.Generate(Secret, 0, digits: 8);
        code.Should().HaveLength(8);
    }
}
