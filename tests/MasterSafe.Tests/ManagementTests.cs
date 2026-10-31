// File:    ManagementTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using FluentAssertions;
using MasterSafe.App;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public sealed class ManagementTests : IDisposable
{
    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private const string OldPassword = "the old master password";
    private const string NewPassword = "a brand new master password";
    private const string KeyPassphrase = "the recovery key passphrase";

    private static readonly VaultContents Sample = new()
    {
        Accounts = [new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }],
        Passwords = [new PasswordEntry { Title = "Mail", UserName = "a@b.c", Password = "p@ssw0rd!" }],
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms-mgmt-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly SwitchableVaultRepository _repository;
    private readonly VaultCatalog _catalog;
    private readonly VaultService _vault;
    private readonly AppServices _services;

    public ManagementTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(_paths.DataFolder);
        _settings = new SettingsStore(_paths.SettingsPath);
        _repository = new SwitchableVaultRepository(_paths.DefaultVaultPath);
        _catalog = new VaultCatalog(_paths, _repository, _settings);
        var encryption = new AesGcmEncryptionService();
        var passwords = new MasterPasswordService();
        _vault = new VaultService(_repository, encryption, passwords, FastKdf);
        var hybrid = PostQuantumProviderFactory.CreateHybrid();
        _services = new AppServices(
            _paths,
            _catalog,
            _settings,
            _dialogs,
            new RecoveryKeyService(encryption, passwords, hybrid, FastKdf),
            new RecipientBackupService(encryption, hybrid));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private MainWindowViewModel NewShell() => new(_vault, services: _services);

    // ---------------------------------------------------------------- settings

    [Fact]
    public void Settings_ArePersistedAndReloaded()
    {
        var vm = new SettingsViewModel(_settings)
        {
            SelectedTheme = AppTheme.Dark,
            MinimizeToTray = false,
            AutoLockMinutes = 20,
        };
        vm.Should().NotBeNull();

        var reloaded = new SettingsViewModel(_settings);
        reloaded.SelectedTheme.Should().Be(AppTheme.Dark);
        reloaded.MinimizeToTray.Should().BeFalse();
        reloaded.AutoLockMinutes.Should().Be(20);
    }

    [Fact]
    public void SavingSettings_KeepsRecoveryKeyInformation()
    {
        var saved = _settings.Load();
        saved.RecoveryPublicKey = "AAAA";
        _settings.Save(saved);

        _ = new SettingsViewModel(_settings) { AutoLockMinutes = 9 };

        _settings.Load().RecoveryPublicKey.Should().Be("AAAA");
    }

    // ------------------------------------------------------ delete confirmation

    [Fact]
    public void DeletingAnAccount_NeedsConfirmation()
    {
        var dashboard = new DashboardViewModel(startTimer: false);
        dashboard.Load(Sample.Accounts);
        var item = dashboard.Accounts.Single();

        dashboard.RequestDeleteCommand.Execute(item);
        dashboard.HasPendingDelete.Should().BeTrue();
        dashboard.Accounts.Should().HaveCount(1);

        dashboard.CancelDeleteCommand.Execute(null);
        dashboard.HasPendingDelete.Should().BeFalse();
        dashboard.Accounts.Should().HaveCount(1);

        dashboard.RequestDeleteCommand.Execute(item);
        dashboard.ConfirmDeleteCommand.Execute(null);
        dashboard.Accounts.Should().BeEmpty();
        dashboard.HasPendingDelete.Should().BeFalse();
    }

    [Fact]
    public void DeletingAPassword_NeedsConfirmation()
    {
        var passwords = new PasswordsViewModel();
        passwords.Load(Sample.Passwords);
        var entry = passwords.Entries.Single();

        passwords.RequestDeleteCommand.Execute(entry);
        passwords.HasPendingDelete.Should().BeTrue();
        passwords.Entries.Should().HaveCount(1);

        passwords.CancelDeleteCommand.Execute(null);
        passwords.Entries.Should().HaveCount(1);

        passwords.RequestDeleteCommand.Execute(entry);
        passwords.ConfirmDeleteCommand.Execute(null);
        passwords.Entries.Should().BeEmpty();
    }

    // ------------------------------------------------------------ password policy

    [Fact]
    public void NewMasterPassword_NeedsTwelveCharacters()
    {
        var login = new LoginViewModel { IsCreateMode = true };
        string? requested = null;
        login.UnlockRequested += (_, password) => requested = password;

        login.MasterPassword = "elevenchars";
        login.ConfirmPassword = "elevenchars";
        login.UnlockCommand.Execute(null);
        login.Error.Should().Contain("12");
        requested.Should().BeNull();
        login.PasswordHint.Should().Contain("1 more");

        login.MasterPassword = "twelve chars";
        login.ConfirmPassword = "twelve chars";
        login.UnlockCommand.Execute(null);
        requested.Should().Be("twelve chars");
    }

    // -------------------------------------------------------------------- vaults

    [Fact]
    public async Task VaultsPage_ListsStartsNewOpensAndDeletes()
    {
        await _vault.SaveContentsAsync(OldPassword, Sample);
        var changed = 0;
        var page = new VaultsViewModel(_services) { ActiveVaultChanged = () => { changed++; return Task.CompletedTask; } };

        page.Vaults.Should().ContainSingle().Which.IsActive.Should().BeTrue();
        page.DataFolder.Should().Be(_paths.DataFolder);

        // Start a new vault: the old one is archived, never deleted.
        page.RequestStartNewCommand.Execute(null);
        page.ConfirmingNew.Should().BeTrue();
        await page.ConfirmStartNewCommand.ExecuteAsync(null);
        changed.Should().Be(1);
        File.Exists(_paths.DefaultVaultPath).Should().BeFalse();
        var archived = page.Vaults.Should().ContainSingle(v => v.Description.StartsWith("Archived", StringComparison.Ordinal)).Subject;
        archived.CanDelete.Should().BeTrue();

        // The old vault can be opened again and still unlocks with its own password.
        await page.OpenRowCommand.ExecuteAsync(archived);
        changed.Should().Be(2);
        _catalog.ActivePath.Should().Be(archived.Path);
        (await _vault.OpenContentsAsync(OldPassword)).Passwords.Should().HaveCount(1);

        // Deleting needs a confirmation step.
        page.RequestDeleteCommand.Execute(page.Vaults.Single(v => v.Path == archived.Path));
        page.Vaults.Single(v => v.Path == archived.Path).IsConfirmingDelete.Should().BeTrue();
        File.Exists(archived.Path).Should().BeTrue();

        await page.ConfirmDeleteCommand.ExecuteAsync(page.Vaults.Single(v => v.Path == archived.Path));
        File.Exists(archived.Path).Should().BeFalse();
        changed.Should().Be(3);
    }

    [Fact]
    public async Task VaultsPage_OpenFile_UsesTheChosenFileInPlace()
    {
        var external = Path.Combine(_root, "usb", "friend.sauth");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        var other = new VaultService(new FileVaultRepository(external), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);
        await other.SaveContentsAsync("friends password 123", Sample);

        _dialogs.OpenResult = external;
        var page = new VaultsViewModel(_services);
        await page.OpenFileCommand.ExecuteAsync(null);

        _catalog.ActivePath.Should().Be(Path.GetFullPath(external));
        var row = page.Vaults.Single(v => v.IsActive);
        row.CanDelete.Should().BeFalse();
        row.CanForget.Should().BeTrue();
        (await _vault.OpenContentsAsync("friends password 123")).Accounts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ShellLocksAndReloadsWhenTheActiveVaultChanges()
    {
        await _vault.SaveContentsAsync(OldPassword, Sample);
        var shell = NewShell();
        await shell.UnlockVaultAsync(OldPassword);
        shell.IsUnlocked.Should().BeTrue();
        shell.Dashboard.Accounts.Should().HaveCount(1);

        shell.Vaults.RequestStartNewCommand.Execute(null);
        await shell.Vaults.ConfirmStartNewCommand.ExecuteAsync(null);

        shell.IsUnlocked.Should().BeFalse();
        shell.Dashboard.Accounts.Should().BeEmpty();
        shell.Login.IsCreateMode.Should().BeTrue();
        File.Exists(_paths.DefaultVaultPath).Should().BeFalse();
    }

    // ---------------------------------------------------------- forgot password

    [Fact]
    public async Task ForgotPassword_StartNew_ArchivesTheOldVault()
    {
        await _vault.SaveContentsAsync(OldPassword, Sample);
        var shell = NewShell();
        await Task.Delay(50);

        shell.Login.ShowForgotCommand.Execute(null);
        shell.Login.Forgot.IsOpen.Should().BeTrue();

        shell.Login.Forgot.RequestStartNewCommand.Execute(null);
        shell.Login.Forgot.ConfirmingNew.Should().BeTrue();
        await shell.Login.Forgot.StartNewCommand.ExecuteAsync(null);

        shell.Login.Forgot.IsOpen.Should().BeFalse();
        shell.Login.IsCreateMode.Should().BeTrue();
        Directory.GetFiles(_paths.ArchiveFolder, "*.sauth").Should().ContainSingle();

        // Creating the new vault works and the archived one is still there to open later.
        await shell.UnlockVaultAsync(NewPassword);
        shell.IsUnlocked.Should().BeTrue();
        shell.Dashboard.Accounts.Should().BeEmpty();
        shell.Vaults.Refresh();
        shell.Vaults.Vaults.Should().HaveCount(2);
    }

    [Fact]
    public async Task ForgotPassword_RestoreFromRecoveryBackup_RebuildsTheVaultWithANewPassword()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        // Before: a vault, a recovery key and a recovery backup of its contents.
        await _vault.SaveContentsAsync(OldPassword, Sample);
        var created = _services.RecoveryKeys.Create(KeyPassphrase);
        var keyFile = Path.Combine(_root, "keys", "recovery.msrk");
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        await File.WriteAllBytesAsync(keyFile, created.FileBytes);
        var backupFolder = Path.Combine(_root, "backups");
        var backupPath = await _services.RecoveryBackups.ExportContentsAsync(Sample, created.PublicKey, new LocalFolderBackupProvider(backupFolder));

        // The master password is now "lost": restore with the key.
        var shell = NewShell();
        await Task.Delay(50);
        var forgot = shell.Login.Forgot;
        forgot.IsOpen = true;
        forgot.BackupFilePath = backupPath;
        forgot.KeyFilePath = keyFile;
        forgot.KeyPassphrase = KeyPassphrase;
        forgot.NewPassword = NewPassword;
        forgot.ConfirmPassword = NewPassword;

        await forgot.RestoreCommand.ExecuteAsync(null);

        shell.IsUnlocked.Should().BeTrue();
        shell.Dashboard.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("GitHub");
        shell.Passwords.Entries.Should().ContainSingle().Which.Password.Should().Be("p@ssw0rd!");
        (await _vault.OpenContentsAsync(NewPassword)).Passwords.Should().HaveCount(1);

        // The old vault was moved aside, not destroyed.
        var archive = Directory.GetFiles(_paths.ArchiveFolder, "*.sauth").Should().ContainSingle().Subject;
        var oldVault = new VaultService(new FileVaultRepository(archive), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);
        (await oldVault.OpenContentsAsync(OldPassword)).Accounts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ForgotPassword_WrongKeyPassphrase_ChangesNothing()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        await _vault.SaveContentsAsync(OldPassword, Sample);
        var created = _services.RecoveryKeys.Create(KeyPassphrase);
        var keyFile = Path.Combine(_root, "recovery.msrk");
        await File.WriteAllBytesAsync(keyFile, created.FileBytes);
        var backupPath = await _services.RecoveryBackups.ExportContentsAsync(
            Sample, created.PublicKey, new LocalFolderBackupProvider(Path.Combine(_root, "backups")));

        var forgot = new ForgotPasswordViewModel(_services, _vault)
        {
            BackupFilePath = backupPath,
            KeyFilePath = keyFile,
            KeyPassphrase = "this is not the passphrase",
            NewPassword = NewPassword,
            ConfirmPassword = NewPassword,
        };

        await forgot.RestoreCommand.ExecuteAsync(null);

        forgot.Status.Should().Contain("cannot open");
        File.Exists(_paths.DefaultVaultPath).Should().BeTrue();
        Directory.Exists(_paths.ArchiveFolder).Should().BeFalse();
        (await _vault.OpenContentsAsync(OldPassword)).Accounts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ForgotPassword_RestoreChecksTheNewPassword()
    {
        var forgot = new ForgotPasswordViewModel(_services, _vault)
        {
            BackupFilePath = "x.sauth",
            KeyFilePath = "y.msrk",
            KeyPassphrase = "z",
            NewPassword = "short",
            ConfirmPassword = "short",
        };
        await forgot.RestoreCommand.ExecuteAsync(null);
        forgot.Status.Should().Contain("12 characters");

        forgot.NewPassword = NewPassword;
        forgot.ConfirmPassword = "something else entirely";
        await forgot.RestoreCommand.ExecuteAsync(null);
        forgot.Status.Should().Contain("do not match");
    }

    // ------------------------------------------------------------------ recovery

    [Fact]
    public async Task Recovery_CreateKey_SavesTheFileFirst_AndThenRemembersThePublicKey()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keyFile = Path.Combine(_root, "saved", "my.msrk");
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        _dialogs.SaveResult = keyFile;
        var page = new RecoveryViewModel(_services, () => Sample);
        page.HasKey.Should().BeFalse();
        page.CanCreateKey.Should().BeTrue();

        page.Passphrase = "short";
        page.ConfirmPassphrase = "short";
        await page.CreateKeyCommand.ExecuteAsync(null);
        page.Status.Should().Contain("12 characters");
        page.Passphrase = KeyPassphrase;
        page.ConfirmPassphrase = "not the same one";
        await page.CreateKeyCommand.ExecuteAsync(null);
        page.Status.Should().Contain("do not match");
        File.Exists(keyFile).Should().BeFalse();

        page.ConfirmPassphrase = KeyPassphrase;
        await page.CreateKeyCommand.ExecuteAsync(null);

        File.Exists(keyFile).Should().BeTrue();
        page.HasKey.Should().BeTrue();
        _settings.Load().RecoveryPublicKey.Should().NotBeNullOrEmpty();
        page.Passphrase.Should().BeEmpty();

        // The saved file really holds the matching private key.
        var info = _services.RecoveryKeys.ReadPublicInfo(await File.ReadAllBytesAsync(keyFile));
        Convert.ToBase64String(info.PublicKey).Should().Be(_settings.Load().RecoveryPublicKey);
        _services.RecoveryKeys.OpenPrivateKey(await File.ReadAllBytesAsync(keyFile), KeyPassphrase).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Recovery_CancellingTheSaveDialog_DoesNotSetUpTheKey()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        _dialogs.SaveResult = null;
        var page = new RecoveryViewModel(_services, () => Sample)
        {
            Passphrase = KeyPassphrase,
            ConfirmPassphrase = KeyPassphrase,
        };

        await page.CreateKeyCommand.ExecuteAsync(null);

        page.HasKey.Should().BeFalse();
        _settings.Load().RecoveryPublicKey.Should().BeNull();
        page.Status.Should().Contain("not saved");
    }

    [Fact]
    public async Task Recovery_CreateBackup_NeedsAKey_AndWritesAnEncryptedFile()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(_root, "recovery-backups");
        _dialogs.FolderResult = folder;
        var page = new RecoveryViewModel(_services, () => Sample);

        await page.CreateBackupCommand.ExecuteAsync(null);
        page.Status.Should().Contain("Create a recovery key first");

        _dialogs.SaveResult = Path.Combine(_root, "k.msrk");
        page.Passphrase = KeyPassphrase;
        page.ConfirmPassphrase = KeyPassphrase;
        await page.CreateKeyCommand.ExecuteAsync(null);

        await page.CreateBackupCommand.ExecuteAsync(null);

        var file = Directory.GetFiles(folder, "recovery-*.sauth").Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(file)).Should().NotContain("p@ssw0rd!").And.NotContain("JBSWY3DPEHPK3PXP");
        _settings.Load().LastRecoveryBackupUtc.Should().NotBeNullOrEmpty();
        page.LastBackupInfo.Should().StartWith("Last recovery backup");
    }

    [Fact]
    public void Recovery_WithoutServices_ReportsUnavailable()
    {
        var page = new RecoveryViewModel();
        page.IsAvailable.Should().BeFalse();
        page.CanCreateKey.Should().BeFalse();
        new VaultsViewModel().IsAvailable.Should().BeFalse();
        new ForgotPasswordViewModel().IsAvailable.Should().BeFalse();
    }

    private sealed class FakeDialogs : IFileDialogs
    {
        public string? OpenResult { get; set; }

        public string? SaveResult { get; set; }

        public string? FolderResult { get; set; }

        public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult(OpenResult);

        public Task<string?> SaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult(SaveResult);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderResult);

        public void ShowInFolder(string path)
        {
        }
    }
}
