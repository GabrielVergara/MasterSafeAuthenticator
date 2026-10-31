// File:    VaultContentsTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;
using Xunit;

namespace SafeAuthenticator.Tests;

public class VaultContentsTests
{
    private static readonly Argon2Parameters FastKdf = new() { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };

    [Fact]
    public async Task Contents_RoundTrip_PreservesPasswords()
    {
        var repo = new MemoryRepo();
        var vault = CreateVault(repo);
        var contents = new VaultContents
        {
            Accounts = [new OtpAccount { Issuer = "GitHub", Secret = "JBSWY3DPEHPK3PXP" }],
            Passwords = [new PasswordEntry { Title = "Mail", UserName = "a@b.c", Password = "p@ss", Tags = ["personal"] }],
        };
        await vault.SaveContentsAsync("pw", contents);
        var loaded = await vault.OpenContentsAsync("pw");
        loaded.Accounts.Should().HaveCount(1);
        loaded.Passwords.Should().ContainSingle().Which.Password.Should().Be("p@ss");
        (await repo.LoadAsync())!.Version.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_KeepsExistingPasswords()
    {
        var vault = CreateVault(new MemoryRepo());
        await vault.SaveContentsAsync("pw", new VaultContents { Passwords = [new PasswordEntry { Title = "Mail", Password = "x" }] });
        await vault.SaveAsync("pw", [new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" }]);
        var loaded = await vault.OpenContentsAsync("pw");
        loaded.Accounts.Should().HaveCount(1);
        loaded.Passwords.Should().HaveCount(1);
    }

    [Fact]
    public async Task Rotation_PreservesPasswords()
    {
        var vault = CreateVault(new MemoryRepo());
        await vault.SaveContentsAsync("old", new VaultContents { Passwords = [new PasswordEntry { Title = "Mail", Password = "x" }] });
        await vault.RotateMasterPasswordAsync("old", "new");
        (await vault.OpenContentsAsync("new")).Passwords.Should().HaveCount(1);
    }

    [Fact]
    public async Task V1Vault_IsStillReadable()
    {
        var repo = new MemoryRepo();
        var enc = new AesGcmEncryptionService();
        var mp = new MasterPasswordService();
        var salt = mp.GenerateSalt();
        var key = mp.DeriveKey("pw", salt, FastKdf);
        var json = JsonSerializer.SerializeToUtf8Bytes(new List<OtpAccount> { new() { Issuer = "Old", Secret = "JBSWY3DPEHPK3PXP" } });
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) { gz.Write(json); }
        var payload = enc.Encrypt(ms.ToArray(), key);
        await repo.SaveAsync(new VaultFile
        {
            Version = 1,
            Salt = Convert.ToBase64String(salt),
            KdfParameters = FastKdf,
            Nonce = Convert.ToBase64String(payload.Nonce),
            Tag = Convert.ToBase64String(payload.Tag),
            Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            Compressed = true,
        });
        var loaded = await CreateVault(repo).OpenContentsAsync("pw");
        loaded.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("Old");
        loaded.Passwords.Should().BeEmpty();
    }

    private static VaultService CreateVault(IVaultRepository repo)
        => new(repo, new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    private sealed class MemoryRepo : IVaultRepository
    {
        private VaultFile? _vault;
        public Task<VaultFile?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_vault);
        public Task SaveAsync(VaultFile vault, CancellationToken cancellationToken = default) { _vault = vault; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_vault is not null);
    }
}
