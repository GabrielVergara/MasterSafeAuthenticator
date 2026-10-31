// File:    PostQuantumTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public class PostQuantumTests
{
    [Fact]
    public void Factory_ReturnsProvider_WithAlgorithmName()
    {
        var provider = PostQuantumProviderFactory.Create();
        provider.AlgorithmName.Should().Be("ML-KEM-768");
    }

    [Fact]
    public void UnavailableProvider_ThrowsRatherThanFakingCrypto()
    {
        var provider = new UnavailableMlKemProvider();
        provider.IsAvailable.Should().BeFalse();

        var act = () => provider.GenerateKeyPair();
        act.Should().Throw<NotSupportedException>();
    }
}
