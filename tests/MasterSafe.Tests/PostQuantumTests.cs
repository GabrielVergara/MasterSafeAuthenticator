// File:    PostQuantumTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using FluentAssertions;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public class PostQuantumTests
{
    private static readonly bool Supported = MLKem.IsSupported;

    [Fact]
    public void Factory_ReturnsProvider_WithAlgorithmName()
    {
        var provider = PostQuantumProviderFactory.Create();
        provider.AlgorithmName.Should().Be("ML-KEM-768");
        provider.IsAvailable.Should().Be(Supported);
    }

    [Fact]
    public void UnavailableProvider_ThrowsRatherThanFakingCrypto()
    {
        var provider = new UnavailableMlKemProvider();
        provider.IsAvailable.Should().BeFalse();

        var act = () => provider.GenerateKeyPair();
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void MlKem_RoundTrip_AgreesOnTheSharedSecret()
    {
        if (!Supported)
        {
            return;
        }

        var kem = new MlKemProvider();
        var pair = kem.GenerateKeyPair();
        var enc = kem.Encapsulate(pair.PublicKey);

        enc.Ciphertext.Should().HaveCount(1088);
        enc.SharedSecret.Should().HaveCount(32);
        kem.Decapsulate(pair.PrivateKey, enc.Ciphertext).Should().Equal(enc.SharedSecret);
    }

    [Fact]
    public void Hybrid_RoundTrip_AgreesOnTheSharedSecret()
    {
        if (!Supported)
        {
            return;
        }

        var kem = PostQuantumProviderFactory.CreateHybrid();
        kem.AlgorithmName.Should().Be("ECDH-P384+ML-KEM-768");
        var pair = kem.GenerateKeyPair();

        var enc = kem.Encapsulate(pair.PublicKey);
        enc.SharedSecret.Should().HaveCount(32);
        kem.Decapsulate(pair.PrivateKey, enc.Ciphertext).Should().Equal(enc.SharedSecret);
    }

    [Fact]
    public void Hybrid_EachEncapsulation_IsFresh()
    {
        if (!Supported)
        {
            return;
        }

        var kem = new HybridKemProvider();
        var pair = kem.GenerateKeyPair();
        var a = kem.Encapsulate(pair.PublicKey);
        var b = kem.Encapsulate(pair.PublicKey);

        a.SharedSecret.Should().NotEqual(b.SharedSecret);
        a.Ciphertext.Should().NotEqual(b.Ciphertext);
    }

    [Fact]
    public void Hybrid_WrongRecipient_CannotRecoverTheSecret()
    {
        if (!Supported)
        {
            return;
        }

        var kem = new HybridKemProvider();
        var alice = kem.GenerateKeyPair();
        var mallory = kem.GenerateKeyPair();
        var enc = kem.Encapsulate(alice.PublicKey);

        // ML-KEM decapsulation never fails, it returns an unrelated secret, so the result must differ.
        var wrong = kem.Decapsulate(mallory.PrivateKey, enc.Ciphertext);
        wrong.Should().NotEqual(enc.SharedSecret);
    }

    [Fact]
    public void Hybrid_TamperedCiphertext_GivesADifferentSecretOrFails()
    {
        if (!Supported)
        {
            return;
        }

        var kem = new HybridKemProvider();
        var pair = kem.GenerateKeyPair();
        var enc = kem.Encapsulate(pair.PublicKey);

        var tampered = (byte[])enc.Ciphertext.Clone();
        tampered[^1] ^= 0x01;

        try
        {
            kem.Decapsulate(pair.PrivateKey, tampered).Should().NotEqual(enc.SharedSecret);
        }
        catch (CryptographicException)
        {
            // Also acceptable: the input is rejected outright.
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(10)]
    public void Hybrid_MalformedInput_IsRejected(int length)
    {
        if (!Supported)
        {
            return;
        }

        var kem = new HybridKemProvider();
        var act = () => kem.Encapsulate(new byte[length]);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ReleaseSigner_SignsAndVerifies_AndDetectsChanges()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var (publicKey, seed) = ReleaseSigner.GenerateKeyPair();
        var file = "installer bytes"u8.ToArray();

        var signature = ReleaseSigner.Sign(file, seed);

        ReleaseSigner.Verify(file, signature, publicKey).Should().BeTrue();

        var changed = (byte[])file.Clone();
        changed[0] ^= 1;
        ReleaseSigner.Verify(changed, signature, publicKey).Should().BeFalse();

        var other = ReleaseSigner.GenerateKeyPair();
        ReleaseSigner.Verify(file, signature, other.PublicKey).Should().BeFalse();
    }
}
