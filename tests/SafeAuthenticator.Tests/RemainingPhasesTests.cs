// File:    RemainingPhasesTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using FluentAssertions;
using SafeAuthenticator.App.ViewModels;
using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;
using SafeAuthenticator.Platform;
using Xunit;

namespace SafeAuthenticator.Tests;

public class RemainingPhasesTests
{
    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private static VaultService NewVault(string path)
        => new(new FileVaultRepository(path), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    [Fact]
    public void Generator_ProducesRequestedLength_AndAllClasses()
    {
        var pw = PasswordGenerator.Generate(24);

        pw.Should().HaveLength(24);
        pw.Any(char.IsLower).Should().BeTrue();
        pw.Any(char.IsUpper).Should().BeTrue();
        pw.Any(char.IsDigit).Should().BeTrue();
        pw.Any(c => !char.IsLetterOrDigit(c)).Should().BeTrue();
        PasswordGenerator.Generate(24).Should().NotBe(pw);
    }

    [Fact]
    public void Generator_RejectsTooShort()
    {
        var act = () => PasswordGenerator.Generate(4);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PasswordEntry_RequiresTitleAndPassword()
    {
        new PasswordEntry { Title = "", Password = "x" }.IsValid(out _).Should().BeFalse();
        new PasswordEntry { Title = "a", Password = "" }.IsValid(out _).Should().BeFalse();
        new PasswordEntry { Title = "a", Password = "x" }.IsValid(out _).Should().BeTrue();
    }

    [Fact]
    public void PasswordsVm_SaveFilterEditDelete()
    {
        var vm = new PasswordsViewModel();
        var changes = 0;
        vm.EntriesChanged += (_, _) => changes++;

        vm.Title = "GitHub";
        vm.UserName = "alice";
        vm.Password = "s3cret";
        vm.SaveCommand.Execute(null);
        vm.Title = "Bank";
        vm.Password = "pw2";
        vm.SaveCommand.Execute(null);

        vm.Entries.Should().HaveCount(2);
        vm.SearchText = "git";
        vm.Entries.Should().ContainSingle().Which.Title.Should().Be("GitHub");

        vm.EditCommand.Execute(vm.Entries[0]);
        vm.Password = "changed";
        vm.SaveCommand.Execute(null);
        vm.Snapshot().Should().HaveCount(2);
        vm.Snapshot().Single(e => e.Title == "GitHub").Password.Should().Be("changed");

        vm.DeleteCommand.Execute(vm.Entries[0]);
        vm.Snapshot().Should().ContainSingle();
        changes.Should().Be(4);
    }

    [Fact]
    public void PasswordsVm_RejectsInvalidEntry()
    {
        var vm = new PasswordsViewModel { Title = "", Password = "x" };

        vm.SaveCommand.Execute(null);

        vm.Snapshot().Should().BeEmpty();
        vm.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task KeyBasedVaultOperations_RoundTrip()
    {
        var path = TempPath();
        try
        {
            var vault = NewVault(path);
            await vault.CreateAsync("master-password");
            var key = await vault.DeriveKeyAsync("master-password");

            await vault.SaveWithKeyAsync(key, new VaultContents
            {
                Passwords = [new PasswordEntry { Title = "Mail", Password = "x" }],
            });
            var loaded = await vault.OpenWithKeyAsync(key);

            loaded.Passwords.Should().ContainSingle();
            (await vault.OpenContentsAsync("master-password")).Passwords.Should().ContainSingle();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DeriveKey_WrongPassword_Throws()
    {
        var path = TempPath();
        try
        {
            var vault = NewVault(path);
            await vault.CreateAsync("master-password");

            var act = async () => await vault.DeriveKeyAsync("nope");
            await act.Should().ThrowAsync<InvalidPasswordException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PasswordsAndAccounts_SurviveRestart()
    {
        var path = TempPath();
        try
        {
            var vm = new MainWindowViewModel(NewVault(path), new HeadlessCredentialStore());
            await vm.UnlockVaultAsync("master-password");
            vm.Passwords.Title = "Mail";
            vm.Passwords.Password = "pw";
            vm.Passwords.SaveCommand.Execute(null);
            await vm.PersistAsync();

            var vm2 = new MainWindowViewModel(NewVault(path), new HeadlessCredentialStore());
            await vm2.UnlockVaultAsync("master-password");

            vm2.Passwords.Snapshot().Should().ContainSingle().Which.Title.Should().Be("Mail");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ChangeMasterPassword_ReencryptsAndKeepsData()
    {
        var path = TempPath();
        try
        {
            var vault = NewVault(path);
            var vm = new MainWindowViewModel(vault, new HeadlessCredentialStore());
            await vm.UnlockVaultAsync("old-password");
            vm.Passwords.Title = "Mail";
            vm.Passwords.Password = "pw";
            vm.Passwords.SaveCommand.Execute(null);
            await vm.PersistAsync();

            var error = await vm.ChangeMasterPasswordAsync("old-password", "new-password");

            error.Should().BeNull();
            (await vault.OpenContentsAsync("new-password")).Passwords.Should().ContainSingle();
            var act = async () => await vault.OpenContentsAsync("old-password");
            await act.Should().ThrowAsync<InvalidPasswordException>();

            // Later saves still work with the new key.
            vm.Passwords.Title = "Bank";
            vm.Passwords.Password = "pw2";
            vm.Passwords.SaveCommand.Execute(null);
            await vm.PersistAsync();
            (await vault.OpenContentsAsync("new-password")).Passwords.Should().HaveCount(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ChangeMasterPassword_WrongCurrent_ReturnsError()
    {
        var path = TempPath();
        try
        {
            var vm = new MainWindowViewModel(NewVault(path), new HeadlessCredentialStore());
            await vm.UnlockVaultAsync("old-password");

            var error = await vm.ChangeMasterPasswordAsync("wrong", "new-password");

            error.Should().Contain("incorrect");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Settings_ValidatesNewPassword()
    {
        var vm = new SettingsViewModel { NewPassword = "short", ConfirmNewPassword = "short" };
        vm.ChangePasswordHandler = (_, _) => Task.FromResult<string?>(null);

        await vm.ChangePasswordCommand.ExecuteAsync(null);
        vm.PasswordChangeStatus.Should().Contain("at least");

        vm.NewPassword = "long-enough-1";
        vm.ConfirmNewPassword = "different-1";
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        vm.PasswordChangeStatus.Should().Contain("do not match");

        vm.ConfirmNewPassword = "long-enough-1";
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        vm.PasswordChangeStatus.Should().Contain("changed");
    }

    [Fact]
    public async Task QuickUnlock_UsesStoredKey_AndRevokesOnChange()
    {
        var path = TempPath();
        try
        {
            var vault = NewVault(path);
            var store = new FakeSupportedStore();
            var vm = new MainWindowViewModel(vault, store);
            vm.Login.RememberMe = true;
            await vm.UnlockVaultAsync("master-password");
            store.Read(MainWindowViewModel.QuickUnlockTarget).Should().NotBeNull();

            vm.LockCommand.Execute(null);
            vm.IsUnlocked.Should().BeFalse();
            vm.Login.HasQuickUnlock.Should().BeTrue();

            await vm.QuickUnlockAsync();
            vm.IsUnlocked.Should().BeTrue();

            var oldKey = store.Read(MainWindowViewModel.QuickUnlockTarget);
            await vm.ChangeMasterPasswordAsync("master-password", "another-password");
            store.Read(MainWindowViewModel.QuickUnlockTarget).Should().NotEqual(oldKey);

            vm.LockCommand.Execute(null);
            await vm.QuickUnlockAsync();
            vm.IsUnlocked.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task QuickUnlock_StaleKey_IsDeleted()
    {
        var path = TempPath();
        try
        {
            var vault = NewVault(path);
            await vault.CreateAsync("master-password");
            var store = new FakeSupportedStore();
            store.Save(MainWindowViewModel.QuickUnlockTarget, new byte[32]);
            var vm = new MainWindowViewModel(vault, store);

            await vm.QuickUnlockAsync();

            vm.IsUnlocked.Should().BeFalse();
            store.Read(MainWindowViewModel.QuickUnlockTarget).Should().BeNull();
            vm.Login.Error.Should().Contain("expired");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeSupportedStore : ICredentialStore
    {
        private readonly Dictionary<string, byte[]> _items = [];

        public bool IsSupported => true;

        public void Save(string target, byte[] secret) => _items[target] = (byte[])secret.Clone();

        public byte[]? Read(string target) => _items.TryGetValue(target, out var v) ? (byte[])v.Clone() : null;

        public bool Delete(string target) => _items.Remove(target);
    }
}
