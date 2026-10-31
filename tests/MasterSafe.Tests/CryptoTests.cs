// File:    CryptoTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using Moq;
using MasterSafe.Core;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public class CryptoTests
{
    // Fast Argon2 parameters to keep the test suite responsive.
    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    [Fact]
    public void AesGcm_Roundtrip_Succeeds()
    {
        var svc = new AesGcmEncryptionService();
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        var plaintext = System.Text.Encoding.UTF8.GetBytes("super secret");

        var payload = svc.Encrypt(plaintext, key);
        var decrypted = svc.Decrypt(payload, key);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void AesGcm_TamperedCiphertext_Throws()
    {
        var svc = new AesGcmEncryptionService();
        var key = new byte[32];
        var payload = svc.Encrypt([1, 2, 3, 4], key);
        payload.Ciphertext[0] ^= 0xFF;

        var act = () => svc.Decrypt(payload, key);
        act.Should().Throw<InvalidPasswordException>();
    }

    [Fact]
    public void AesGcm_WrongKeySize_Throws()
    {
        var svc = new AesGcmEncryptionService();
        var act = () => svc.Encrypt([1], new byte[16]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MasterPassword_SameInputs_ProduceSameKey()
    {
        var svc = new MasterPasswordService();
        var salt = svc.GenerateSalt();
        var k1 = svc.DeriveKey("pw", salt, FastKdf);
        var k2 = svc.DeriveKey("pw", salt, FastKdf);
        k1.Should().Equal(k2);
        k1.Should().HaveCount(32);
    }

    [Fact]
    public void MasterPassword_DifferentSalt_ProducesDifferentKey()
    {
        var svc = new MasterPasswordService();
        var k1 = svc.DeriveKey("pw", svc.GenerateSalt(), FastKdf);
        var k2 = svc.DeriveKey("pw", svc.GenerateSalt(), FastKdf);
        k1.Should().NotEqual(k2);
    }

    [Fact]
    public async Task Vault_SaveThenOpen_RoundTrips()
    {
        var repo = new InMemoryVaultRepository();
        var vault = CreateVault(repo);
        var accounts = new[]
        {
            new OtpAccount { Issuer = "GitHub", Label = "alice", Secret = "JBSWY3DPEHPK3PXP" },
            new OtpAccount { Issuer = "AWS", Label = "bob", Secret = "GEZDGNBVGY3TQOJQ" },
        };

        await vault.SaveAsync("master-pw", accounts);
        var loaded = await vault.OpenAsync("master-pw");

        loaded.Should().HaveCount(2);
        loaded[0].Secret.Should().Be("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public async Task Vault_WrongPassword_Throws()
    {
        var repo = new InMemoryVaultRepository();
        var vault = CreateVault(repo);
        await vault.SaveAsync("correct", [new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" }]);

        var act = async () => await vault.OpenAsync("wrong");
        await act.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task Vault_NeverStoresPlaintextSecret()
    {
        var repo = new InMemoryVaultRepository();
        var vault = CreateVault(repo);
        await vault.SaveAsync("pw", [new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" }]);

        var stored = await repo.LoadAsync();
        stored!.Ciphertext.Should().NotContain("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public async Task Vault_KeyRotation_PreservesData()
    {
        var repo = new InMemoryVaultRepository();
        var vault = CreateVault(repo);
        await vault.SaveAsync("old-pw", [new OtpAccount { Issuer = "X", Secret = "JBSWY3DPEHPK3PXP" }]);

        await vault.RotateMasterPasswordAsync("old-pw", "new-pw");

        var loaded = await vault.OpenAsync("new-pw");
        loaded.Should().HaveCount(1);
        loaded[0].Issuer.Should().Be("X");

        var act = async () => await vault.OpenAsync("old-pw");
        await act.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task Vault_UsesRepository()
    {
        var mock = new Mock<IVaultRepository>();
        mock.Setup(r => r.SaveAsync(It.IsAny<VaultFile>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vault = new VaultService(mock.Object, new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);
        await vault.SaveAsync("pw", []);

        mock.Verify(r => r.SaveAsync(It.IsAny<VaultFile>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static VaultService CreateVault(IVaultRepository repo)
        => new(repo, new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    private sealed class InMemoryVaultRepository : IVaultRepository
    {
        private VaultFile? _vault;

        public Task<VaultFile?> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_vault);

        public Task SaveAsync(VaultFile vault, CancellationToken cancellationToken = default)
        {
            _vault = vault;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_vault is not null);
    }
}
