// File:    TotpTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text;
using FluentAssertions;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public class TotpTests
{
    private static readonly byte[] Sha1Secret = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly byte[] Sha256Secret = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    private static readonly byte[] Sha512Secret = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Generate_Sha1_MatchesRfc6238Vectors(long unixTime, string expected)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        var code = TotpGenerator.Generate(Sha1Secret, time, period: 30, digits: 8, algorithm: OtpAlgorithm.Sha1);
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(59L, "46119246")]
    [InlineData(1111111109L, "68084774")]
    [InlineData(2000000000L, "90698825")]
    public void Generate_Sha256_MatchesRfc6238Vectors(long unixTime, string expected)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        var code = TotpGenerator.Generate(Sha256Secret, time, period: 30, digits: 8, algorithm: OtpAlgorithm.Sha256);
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(59L, "90693936")]
    [InlineData(1111111109L, "25091201")]
    [InlineData(2000000000L, "38618901")]
    public void Generate_Sha512_MatchesRfc6238Vectors(long unixTime, string expected)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        var code = TotpGenerator.Generate(Sha512Secret, time, period: 30, digits: 8, algorithm: OtpAlgorithm.Sha512);
        code.Should().Be(expected);
    }

    [Fact]
    public void RemainingSeconds_IsWithinPeriod()
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(45);
        TotpGenerator.RemainingSeconds(time, 30).Should().Be(15);
    }

    [Fact]
    public void Generate_RejectsNonPositivePeriod()
    {
        var act = () => TotpGenerator.Generate(Sha1Secret, DateTimeOffset.UnixEpoch, period: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Generate_SupportsCustomPeriod()
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(120);
        var code = TotpGenerator.Generate(Sha1Secret, time, period: 60);
        code.Should().HaveLength(6);
    }
}
