// File:    KeySplitterTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MasterSafe.Backup;
using Xunit;

namespace MasterSafe.Tests;

public sealed class KeySplitterTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(5000);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public void AnyTwoParts_RebuildTheKey(int a, int b)
    {
        var parts = KeySplitter.Split(Key);

        parts.Should().HaveCount(3);
        KeySplitter.Combine([parts[a], parts[b]]).Should().Equal(Key);
    }

    [Fact]
    public void AllThreeParts_RebuildTheKey_AndTheSameFileTwiceCountsOnce()
    {
        var parts = KeySplitter.Split(Key);

        KeySplitter.Combine(parts).Should().Equal(Key);
        FluentActions.Invoking(() => KeySplitter.Combine([parts[0], parts[0]]))
            .Should().Throw<InvalidDataException>().WithMessage("Add one more key part*");
    }

    [Fact]
    public void OnePart_IsNotTheKey_AndTwoSplitsOfTheSameKeyDiffer()
    {
        var parts = KeySplitter.Split(Key);
        var share = Convert.FromBase64String(JsonSerializer.Deserialize<KeyPart>(parts[0])!.Data);

        share.Should().NotEqual(Key);
        CountEqualBytes(share, Key).Should().BeLessThan(Key.Length / 100, "a share looks random, not like the key");
        KeySplitter.Split(Key)[0].Should().NotEqual(parts[0]);
    }

    [Fact]
    public void Combine_RefusesPartsOfDifferentSplits_AndDamagedParts()
    {
        var a = KeySplitter.Split(Key);
        var b = KeySplitter.Split(Key);

        FluentActions.Invoking(() => KeySplitter.Combine([a[0], b[1]]))
            .Should().Throw<InvalidDataException>().WithMessage("*different recovery keys*");

        var damaged = Damage(a[2]);
        FluentActions.Invoking(() => KeySplitter.Combine([a[0], damaged]))
            .Should().Throw<InvalidDataException>().WithMessage("*do not fit together*");
        FluentActions.Invoking(() => KeySplitter.Combine([a[0], a[1], damaged]))
            .Should().Throw<InvalidDataException>().WithMessage("Key part 3 is damaged*");

        FluentActions.Invoking(() => KeySplitter.Combine([Encoding.UTF8.GetBytes("not a part"), a[1]]))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => KeySplitter.Combine([new byte[300 * 1024], a[1]]))
            .Should().Throw<InvalidDataException>();
    }

    private static byte[] Damage(byte[] partFile)
    {
        var part = JsonSerializer.Deserialize<KeyPart>(partFile)!;
        var data = Convert.FromBase64String(part.Data);
        data[100] ^= 0x01;
        return JsonSerializer.SerializeToUtf8Bytes(new KeyPart
        {
            SetId = part.SetId, Index = part.Index, Threshold = part.Threshold, Count = part.Count, KeyHash = part.KeyHash,
            Data = Convert.ToBase64String(data),
        });
    }

    private static int CountEqualBytes(byte[] a, byte[] b) => a.Zip(b).Count(p => p.First == p.Second);
}
