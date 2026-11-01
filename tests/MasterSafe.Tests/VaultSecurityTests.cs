// File:    VaultSecurityTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text.Json;
using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public class VaultSecurityTests
{
    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private static readonly VaultContents Sample = new()
    {
        Accounts = [new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }],
        Passwords = [new PasswordEntry { Title = "Mail", UserName = "a@b.c", Password = "p@ss" }],
    };

    private static VaultService NewVault(IVaultRepository repo)
        => new(repo, new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sauth");

    [Fact]
    public async Task NewVault_UsesFormat3_WithOnePasswordSlot()
    {
        var repo = new MemoryRepo();
        await NewVault(repo).SaveContentsAsync("pw", Sample);

        var file = (await repo.LoadAsync())!;
        file.Version.Should().Be(3);
        file.VaultId.Should().NotBeNullOrEmpty();
        file.Slots.Should().ContainSingle().Which.Type.Should().Be(SlotTypes.Password);
        file.Salt.Should().BeEmpty();
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        await vault.SaveContentsAsync("pw", Sample);

        var act = () => vault.OpenContentsAsync("nope");
        await act.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task HugeKdfParameters_AreRejected_BeforeAnyWork()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        await vault.SaveContentsAsync("pw", Sample);

        var file = (await repo.LoadAsync())!;
        var slot = file.Slots[0];
        var evil = new KeySlot
        {
            Id = slot.Id,
            Type = slot.Type,
            Label = slot.Label,
            CreatedUtc = slot.CreatedUtc,
            Salt = slot.Salt,
            Nonce = slot.Nonce,
            Tag = slot.Tag,
            WrappedKey = slot.WrappedKey,
            Kdf = new Argon2Parameters { MemorySizeKib = int.MaxValue, Iterations = 1_000_000, DegreeOfParallelism = 255 },
        };
        await repo.SaveAsync(new VaultFile
        {
            Version = 3,
            VaultId = file.VaultId,
            Slots = [evil],
            Nonce = file.Nonce,
            Tag = file.Tag,
            Ciphertext = file.Ciphertext,
        });

        var act = () => vault.OpenContentsAsync("pw");
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task TamperedCiphertext_IsDetected()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        await vault.SaveContentsAsync("pw", Sample);

        var file = (await repo.LoadAsync())!;
        var bytes = Convert.FromBase64String(file.Ciphertext);
        bytes[0] ^= 0x01;
        await repo.SaveAsync(new VaultFile
        {
            Version = 3,
            VaultId = file.VaultId,
            Slots = file.Slots,
            Nonce = file.Nonce,
            Tag = file.Tag,
            Ciphertext = Convert.ToBase64String(bytes),
        });

        var act = () => vault.OpenContentsAsync("pw");
        await act.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task ContentFromAnotherVault_CannotBeSwappedIn()
    {
        var repoA = new MemoryRepo();
        var repoB = new MemoryRepo();
        await NewVault(repoA).SaveContentsAsync("pw", Sample);
        await NewVault(repoB).SaveContentsAsync("pw", new VaultContents());

        var a = (await repoA.LoadAsync())!;
        var b = (await repoB.LoadAsync())!;

        // Vault B's header and slots with vault A's encrypted contents.
        await repoB.SaveAsync(new VaultFile
        {
            Version = 3,
            VaultId = b.VaultId,
            Slots = b.Slots,
            Nonce = a.Nonce,
            Tag = a.Tag,
            Ciphertext = a.Ciphertext,
        });

        var act = () => NewVault(repoB).OpenContentsAsync("pw");
        await act.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task SaveWithWrongKey_DoesNotOverwriteTheVault()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        await vault.SaveContentsAsync("pw", Sample);

        var act = () => vault.SaveWithKeyAsync(new byte[32], new VaultContents());
        await act.Should().ThrowAsync<InvalidPasswordException>();

        (await vault.OpenContentsAsync("pw")).Accounts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ChangePassword_KeepsContents_AndOtherUnlockMethods()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        var hello = new FakeSecretProvider();
        await vault.SaveContentsAsync("old", Sample);
        var key = await vault.DeriveKeyAsync("old");
        await vault.AddSlotAsync(key, hello, "Hello");

        await vault.RotateMasterPasswordAsync("old", "new-password");

        (await vault.OpenContentsAsync("new-password")).Passwords.Should().HaveCount(1);
        var old = () => vault.OpenContentsAsync("old");
        await old.Should().ThrowAsync<InvalidPasswordException>();

        var viaHello = await vault.UnlockWithProviderAsync(hello);
        (await vault.OpenWithKeyAsync(viaHello)).Accounts.Should().HaveCount(1);
    }

    [Fact]
    public async Task UnlockMethod_CanBeAddedUsedAndRemoved_ButNotThePassword()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        var hello = new FakeSecretProvider();
        await vault.SaveContentsAsync("pw", Sample);
        var key = await vault.DeriveKeyAsync("pw");

        await vault.AddSlotAsync(key, hello, "Hello");
        var slots = await vault.ListSlotsAsync();
        slots.Select(s => s.Type).Should().BeEquivalentTo([SlotTypes.Password, SlotTypes.WindowsHello]);

        var viaHello = await vault.UnlockWithProviderAsync(hello);
        viaHello.Should().Equal(key);

        var helloSlot = slots.Single(s => s.Type == SlotTypes.WindowsHello);
        await vault.RemoveSlotAsync(key, helloSlot.Id, hello);
        hello.Removed.Should().Contain(helloSlot.Id);
        var gone = () => vault.UnlockWithProviderAsync(hello);
        await gone.Should().ThrowAsync<InvalidOperationException>();

        var passwordSlot = (await vault.ListSlotsAsync()).Single();
        var act = () => vault.RemoveSlotAsync(key, passwordSlot.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SlotRelabelledAsPassword_IsRejected()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        var hello = new FakeSecretProvider();
        await vault.SaveContentsAsync("pw", Sample);
        var key = await vault.DeriveKeyAsync("pw");
        await vault.AddSlotAsync(key, hello, "Hello");

        // The slot type is part of the authenticated data, so changing it must break unwrapping.
        var file = (await repo.LoadAsync())!;
        var h = file.Slots.Single(s => s.Type == SlotTypes.WindowsHello);
        var forged = new KeySlot
        {
            Id = h.Id,
            Type = SlotTypes.Password,
            Label = h.Label,
            CreatedUtc = h.CreatedUtc,
            Salt = h.Salt,
            Kdf = FastKdf,
            Nonce = h.Nonce,
            Tag = h.Tag,
            WrappedKey = h.WrappedKey,
        };
        await repo.SaveAsync(new VaultFile
        {
            Version = 3,
            VaultId = file.VaultId,
            Slots = [forged],
            Nonce = file.Nonce,
            Tag = file.Tag,
            Ciphertext = file.Ciphertext,
        });

        var act = () => vault.OpenContentsAsync("pw");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task LegacyFormat2Vault_OpensAndUpgradesToFormat3()
    {
        var repo = new MemoryRepo();
        var passwordService = new MasterPasswordService();
        var encryption = new AesGcmEncryptionService();
        var salt = passwordService.GenerateSalt();
        var key = passwordService.DeriveKey("pw", salt, FastKdf);
        var payload = encryption.Encrypt(BoundedGzip.Compress(JsonSerializer.SerializeToUtf8Bytes(Sample)), key);
        await repo.SaveAsync(new VaultFile
        {
            Version = 2,
            Salt = Convert.ToBase64String(salt),
            KdfParameters = FastKdf,
            Nonce = Convert.ToBase64String(payload.Nonce),
            Tag = Convert.ToBase64String(payload.Tag),
            Ciphertext = Convert.ToBase64String(payload.Ciphertext),
            Compressed = true,
        });

        var vault = NewVault(repo);
        (await vault.OpenContentsAsync("pw")).Accounts.Should().HaveCount(1);
        (await repo.LoadAsync())!.Version.Should().Be(2);

        var dataKey = await vault.DeriveKeyAsync("pw");
        (await repo.LoadAsync())!.Version.Should().Be(3);
        (await vault.OpenWithKeyAsync(dataKey)).Passwords.Should().HaveCount(1);
    }

    [Fact]
    public async Task FileRepository_SavesAtomically_AndKeepsPreviousVersion()
    {
        var path = TempPath();
        try
        {
            var repo = new FileVaultRepository(path);
            var vault = NewVault(repo);
            await vault.SaveContentsAsync("pw", Sample);
            File.Exists(repo.BackupPath).Should().BeFalse();

            var key = await vault.DeriveKeyAsync("pw");
            await vault.SaveWithKeyAsync(key, new VaultContents());

            File.Exists(path).Should().BeTrue();
            File.Exists(repo.BackupPath).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse();
            (await vault.OpenContentsAsync("pw")).Accounts.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public async Task FileRepository_RejectsOversizedAndGarbageFiles()
    {
        var path = TempPath();
        try
        {
            var repo = new FileVaultRepository(path);

            await File.WriteAllTextAsync(path, "this is not json");
            var garbage = () => repo.LoadAsync();
            await garbage.Should().ThrowAsync<InvalidDataException>();

            await using (var fs = File.Create(path))
            {
                fs.SetLength(FileVaultRepository.MaxFileBytes + 1);
            }

            var huge = () => repo.LoadAsync();
            await huge.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BoundedGzip_RefusesDecompressionBombs()
    {
        var zeros = new byte[10 * 1024 * 1024];
        var packed = BoundedGzip.Compress(zeros);
        packed.Length.Should().BeLessThan(100_000);

        BoundedGzip.Decompress(packed, zeros.Length).Should().HaveCount(zeros.Length);
        var act = () => BoundedGzip.Decompress(packed, 1024 * 1024);
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0, 3, 4)]
    [InlineData(65536, 0, 4)]
    [InlineData(65536, 3, 0)]
    [InlineData(65536, 3, 4000)]
    [InlineData(Argon2Parameters.MaxMemorySizeKib + 1, 3, 4)]
    [InlineData(65536, Argon2Parameters.MaxIterations + 1, 4)]
    [InlineData(16, 3, 4)]
    public void Argon2Parameters_OutOfRange_AreRejected(int memory, int iterations, int parallelism)
    {
        var p = new Argon2Parameters { MemorySizeKib = memory, Iterations = iterations, DegreeOfParallelism = parallelism };
        var act = p.Validate;
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Argon2Defaults_AreValid_AndStrong()
    {
        Argon2Parameters.Default.Validate();
        Argon2Parameters.Default.MemorySizeKib.Should().BeGreaterThanOrEqualTo(65536);
    }

    // ------------------------------------------------------------------ view model

    [Fact]
    public async Task Lock_WipesDecryptedDataAndSessionKey()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        await vault.SaveContentsAsync("pw", Sample);
        var vm = new MainWindowViewModel(vault);

        await vm.UnlockVaultAsync("pw");
        vm.IsUnlocked.Should().BeTrue();
        vm.Dashboard.Accounts.Should().HaveCount(1);
        vm.Passwords.Entries.Should().HaveCount(1);

        vm.LockCommand.Execute(null);

        vm.IsUnlocked.Should().BeFalse();
        vm.Dashboard.Accounts.Should().BeEmpty();
        vm.Passwords.Entries.Should().BeEmpty();
        vm.Login.MasterPassword.Should().BeEmpty();

        // With the key wiped, a late save does nothing and cannot damage the vault.
        await vm.PersistAsync();
        (await vault.OpenContentsAsync("pw")).Accounts.Should().HaveCount(1);
    }

    [Fact]
    public async Task AutoLock_LocksAfterTheConfiguredIdleTime()
    {
        var now = DateTimeOffset.UtcNow;
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        await vault.SaveContentsAsync("pw", Sample);
        var vm = new MainWindowViewModel(vault, clock: () => now);
        await vm.UnlockVaultAsync("pw");
        vm.Settings.AutoLockMinutes = 5;

        now = now.AddMinutes(4);
        vm.LockIfIdle();
        vm.IsUnlocked.Should().BeTrue();

        vm.NotifyActivity();
        now = now.AddMinutes(4);
        vm.LockIfIdle();
        vm.IsUnlocked.Should().BeTrue();

        now = now.AddMinutes(2);
        vm.LockIfIdle();
        vm.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task AutoLock_WarnsThirtySecondsAhead_AndKeepUnlockedRestartsTheCountdown()
    {
        var now = DateTimeOffset.UtcNow;
        var vault = NewVault(new MemoryRepo());
        await vault.SaveContentsAsync("pw", Sample);
        var vm = new MainWindowViewModel(vault, clock: () => now);
        await vm.UnlockVaultAsync("pw");
        vm.Settings.AutoLockMinutes = 1;

        now = now.AddSeconds(29);
        vm.LockIfIdle();
        vm.IsLockWarningVisible.Should().BeFalse();

        now = now.AddSeconds(5);
        vm.LockIfIdle();
        vm.IsLockWarningVisible.Should().BeTrue();
        vm.LockWarningSeconds.Should().Be(26);

        vm.KeepUnlockedCommand.Execute(null);
        vm.IsLockWarningVisible.Should().BeFalse();
        vm.LockIfIdle();
        vm.IsLockWarningVisible.Should().BeFalse();
        vm.IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task ViewModel_UnlockMethod_FullFlow()
    {
        var repo = new MemoryRepo();
        var vault = NewVault(repo);
        var hello = new FakeSecretProvider();
        await vault.SaveContentsAsync("pw", Sample);
        var vm = new MainWindowViewModel(vault, [hello]);
        await vm.RefreshUnlockMethodsAsync();
        vm.Login.UnlockMethods.Should().BeEmpty();

        await vm.UnlockVaultAsync("pw");
        vm.Settings.AddableMethods.Should().ContainSingle();

        (await vm.AddUnlockMethodAsync(hello)).Should().BeNull();
        vm.Settings.AddableMethods.Should().BeEmpty();
        vm.Settings.UnlockMethods.Should().HaveCount(2);

        vm.LockCommand.Execute(null);
        await vm.RefreshUnlockMethodsAsync();
        vm.Login.UnlockMethods.Should().ContainSingle();

        await vm.UnlockWithMethodAsync(hello);
        vm.IsUnlocked.Should().BeTrue();
        vm.Dashboard.Accounts.Should().HaveCount(1);

        vm.LockCommand.Execute(null);
        hello.Cancel = true;
        await vm.UnlockWithMethodAsync(hello);
        vm.IsUnlocked.Should().BeFalse();
        vm.Login.Error.Should().Contain("cancelled");
    }

    private sealed class MemoryRepo : IVaultRepository
    {
        private VaultFile? _vault;

        public Task<VaultFile?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_vault);

        public Task SaveAsync(VaultFile vault, CancellationToken cancellationToken = default)
        {
            _vault = vault;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_vault is not null);
    }
}
