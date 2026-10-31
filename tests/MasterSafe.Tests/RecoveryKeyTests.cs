// File:    RecoveryKeyTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public class RecoveryKeyTests
{
    private const string Passphrase = "a long recovery passphrase";

    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private static RecoveryKeyService NewKeys()
        => new(new AesGcmEncryptionService(), new MasterPasswordService(), PostQuantumProviderFactory.CreateHybrid(), FastKdf);

    private static RecipientBackupService NewBackups()
        => new(new AesGcmEncryptionService(), PostQuantumProviderFactory.CreateHybrid());

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "ms-rk-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void KeyFile_ExposesPublicKey_AndOpensWithThePassphrase()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var created = keys.Create(Passphrase);

        var info = keys.ReadPublicInfo(created.FileBytes);
        info.PublicKey.Should().Equal(created.PublicKey);
        info.KeyId.Should().Be(created.KeyId);

        var privateKey = keys.OpenPrivateKey(created.FileBytes, Passphrase);
        privateKey.Should().NotBeEmpty();

        // The private key really is the partner of the public key.
        var kem = PostQuantumProviderFactory.CreateHybrid();
        var enc = kem.Encapsulate(created.PublicKey);
        kem.Decapsulate(privateKey, enc.Ciphertext).Should().Equal(enc.SharedSecret);
    }

    [Fact]
    public void KeyFile_PrivateKey_IsNotStoredInTheClear()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var created = keys.Create(Passphrase);
        var privateKey = keys.OpenPrivateKey(created.FileBytes, Passphrase);

        var text = System.Text.Encoding.UTF8.GetString(created.FileBytes);
        text.Should().NotContain(Convert.ToBase64String(privateKey));
    }

    [Fact]
    public void WrongPassphrase_IsRejected()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var created = keys.Create(Passphrase);

        var act = () => keys.OpenPrivateKey(created.FileBytes, "not the right passphrase");
        act.Should().Throw<InvalidPasswordException>();
    }

    [Fact]
    public void ShortPassphrase_IsRefused()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var act = () => NewKeys().Create("short");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SwappingThePublicKey_IsDetected()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var a = keys.Create(Passphrase);
        var b = keys.Create(Passphrase);
        var fileA = JsonSerializer.Deserialize<RecoveryKeyFile>(a.FileBytes)!;
        var fileB = JsonSerializer.Deserialize<RecoveryKeyFile>(b.FileBytes)!;

        // Put B's public key into A's file, which would trick someone into encrypting backups to the wrong key.
        var forged = new RecoveryKeyFile
        {
            Kem = fileA.Kem,
            KeyId = fileA.KeyId,
            CreatedUtc = fileA.CreatedUtc,
            PublicKey = fileB.PublicKey,
            Salt = fileA.Salt,
            Kdf = fileA.Kdf,
            Nonce = fileA.Nonce,
            Tag = fileA.Tag,
            WrappedPrivateKey = fileA.WrappedPrivateKey,
        };

        var act = () => keys.OpenPrivateKey(JsonSerializer.SerializeToUtf8Bytes(forged), Passphrase);
        act.Should().Throw<InvalidPasswordException>();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"Version\":9}")]
    public void NonKeyFiles_AreRejected(string text)
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var act = () => NewKeys().ReadPublicInfo(bytes);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void HostileKeyFileKdfParameters_AreRejected()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var created = keys.Create(Passphrase);
        var file = JsonSerializer.Deserialize<RecoveryKeyFile>(created.FileBytes)!;
        var hostile = new RecoveryKeyFile
        {
            Kem = file.Kem,
            KeyId = file.KeyId,
            CreatedUtc = file.CreatedUtc,
            PublicKey = file.PublicKey,
            Salt = file.Salt,
            Kdf = new Argon2Parameters { MemorySizeKib = int.MaxValue },
            Nonce = file.Nonce,
            Tag = file.Tag,
            WrappedPrivateKey = file.WrappedPrivateKey,
        };

        var act = () => keys.OpenPrivateKey(JsonSerializer.SerializeToUtf8Bytes(hostile), Passphrase);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task RecoveryBackup_CarriesAccountsAndPasswords_AndRestoresThem()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = TempFolder();
        try
        {
            var keys = NewKeys();
            var created = keys.Create(Passphrase);
            var backups = NewBackups();
            var provider = new LocalFolderBackupProvider(folder);
            var contents = new VaultContents
            {
                Accounts = [new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }],
                Passwords = [new PasswordEntry { Title = "Mail", UserName = "a@b.c", Password = "p@ssw0rd!" }],
            };

            await backups.ExportContentsAsync(contents, created.PublicKey, provider);
            var file = (await provider.ListAsync()).Single();

            var text = await File.ReadAllTextAsync(Path.Combine(folder, file));
            text.Should().NotContain("p@ssw0rd!").And.NotContain("JBSWY3DPEHPK3PXP");

            var privateKey = keys.OpenPrivateKey(created.FileBytes, Passphrase);
            var restored = await backups.ImportContentsAsync(file, privateKey, provider);

            restored.Accounts.Should().ContainSingle().Which.Secret.Should().Be("JBSWY3DPEHPK3PXP");
            restored.Passwords.Should().ContainSingle().Which.Password.Should().Be("p@ssw0rd!");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Theory]
    [InlineData("..\\evil.sauth")]
    [InlineData("../evil.sauth")]
    [InlineData("sub/evil.sauth")]
    [InlineData("C:\\Windows\\evil.sauth")]
    [InlineData("/etc/evil.sauth")]
    [InlineData("..")]
    public async Task LocalFolderProvider_RefusesNamesThatEscapeTheFolder(string name)
    {
        var folder = TempFolder();
        try
        {
            var provider = new LocalFolderBackupProvider(folder);

            var write = () => provider.UploadAsync(name, [1, 2, 3]);
            await write.Should().ThrowAsync<ArgumentException>();

            var read = () => provider.DownloadAsync(name);
            await read.Should().ThrowAsync<ArgumentException>();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task LocalFolderProvider_StillWorksWithPlainNames()
    {
        var folder = TempFolder();
        try
        {
            var provider = new LocalFolderBackupProvider(folder);
            await provider.UploadAsync("ok.sauth", [1, 2, 3]);
            (await provider.DownloadAsync("ok.sauth")).Should().Equal(1, 2, 3);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
