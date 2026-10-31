// File:    PersistenceTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public class PersistenceTests
{
    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    [Fact]
    public async Task AddedAccount_IsSavedToVault_AndSurvivesRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var vm = new MainWindowViewModel(NewVault(path));
            await vm.UnlockVaultAsync("master-password");
            vm.Dashboard.Add(new OtpAccount { Issuer = "Acme", Label = "me", Secret = "JBSWY3DPEHPK3PXP" });
            await vm.PersistAsync();

            var vm2 = new MainWindowViewModel(NewVault(path));
            await vm2.UnlockVaultAsync("master-password");

            vm2.Dashboard.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("Acme");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WrongPassword_ShowsError_AndStaysLocked()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await new MainWindowViewModel(NewVault(path)).UnlockVaultAsync("correct-password");

            var vm = new MainWindowViewModel(NewVault(path));
            await vm.UnlockVaultAsync("wrong-password");

            vm.IsUnlocked.Should().BeFalse();
            vm.Login.Error.Should().Contain("Incorrect");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VaultService NewVault(string path)
        => new(new FileVaultRepository(path), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);
}
