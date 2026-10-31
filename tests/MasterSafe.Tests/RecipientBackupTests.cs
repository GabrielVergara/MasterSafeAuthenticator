// File:    RecipientBackupTests.cs
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

public class RecipientBackupTests
{
    private static readonly OtpAccount[] Accounts =
    [
        new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" },
        new OtpAccount { Issuer = "Mail", Label = "x@y.z", Secret = "GEZDGNBVGY3TQOJQ" },
    ];

    private static RecipientBackupService NewService()
        => new(new AesGcmEncryptionService(), PostQuantumProviderFactory.CreateHybrid());

    [Fact]
    public async Task BackupToRecoveryKey_RoundTrips_WithoutAnyPassword()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"ms-recovery-{Guid.NewGuid():N}");
        try
        {
            var service = NewService();
            var keys = service.GenerateRecoveryKeyPair();
            var provider = new LocalFolderBackupProvider(folder);

            await service.ExportAsync(Accounts, keys.PublicKey, provider);
            var file = (await provider.ListAsync()).Single();

            // Nothing readable is stored in the file.
            var text = await File.ReadAllTextAsync(Path.Combine(folder, file));
            text.Should().NotContain("JBSWY3DPEHPK3PXP").And.NotContain("GitHub");

            var restored = await service.ImportAsync(file, keys.PrivateKey, provider);
            restored.Select(a => a.Secret).Should().BeEquivalentTo(Accounts.Select(a => a.Secret));
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
    public async Task BackupToRecoveryKey_CannotBeOpenedWithAnotherKey()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"ms-recovery-{Guid.NewGuid():N}");
        try
        {
            var service = NewService();
            var owner = service.GenerateRecoveryKeyPair();
            var other = service.GenerateRecoveryKeyPair();
            var provider = new LocalFolderBackupProvider(folder);

            await service.ExportAsync(Accounts, owner.PublicKey, provider);
            var file = (await provider.ListAsync()).Single();

            var act = () => service.ImportAsync(file, other.PrivateKey, provider);
            await act.Should().ThrowAsync<InvalidPasswordException>();
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
    public async Task BackupToRecoveryKey_DetectsTamperingAndSwappedIds()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"ms-recovery-{Guid.NewGuid():N}");
        try
        {
            var service = NewService();
            var keys = service.GenerateRecoveryKeyPair();
            var provider = new LocalFolderBackupProvider(folder);

            await service.ExportAsync(Accounts, keys.PublicKey, provider);
            var file = (await provider.ListAsync()).Single();
            var path = Path.Combine(folder, file);
            var envelope = JsonSerializer.Deserialize<RecipientBackupEnvelope>(await File.ReadAllBytesAsync(path))!;

            // Changing the identifier changes the associated data, so decryption must fail.
            var renamed = new RecipientBackupEnvelope
            {
                Id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                Kem = envelope.Kem,
                KemCiphertext = envelope.KemCiphertext,
                WrapNonce = envelope.WrapNonce,
                WrapTag = envelope.WrapTag,
                WrappedKey = envelope.WrappedKey,
                Nonce = envelope.Nonce,
                Tag = envelope.Tag,
                Ciphertext = envelope.Ciphertext,
            };
            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(renamed));

            var act = () => service.ImportAsync(file, keys.PrivateKey, provider);
            await act.Should().ThrowAsync<InvalidPasswordException>();
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
