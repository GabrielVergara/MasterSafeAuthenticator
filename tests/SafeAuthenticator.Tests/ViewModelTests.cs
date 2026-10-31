using System;
using FluentAssertions;
using SafeAuthenticator.App;
using SafeAuthenticator.App.ViewModels;
using SafeAuthenticator.Core;
using Xunit;

namespace SafeAuthenticator.Tests;

public class ViewModelTests
{
    private static OtpAccount DemoAccount() => new()
    {
        Issuer = "SafeAuthenticator",
        Label = "demo@local",
        Secret = "JBSWY3DPEHPK3PXP",
    };

    [Fact]
    public void OtpItem_Update_ProducesCodeAndProgress()
    {
        var item = new OtpItemViewModel(DemoAccount());

        item.Update(DateTimeOffset.UtcNow);

        item.Code.Should().HaveLength(6);
        item.DisplayCode.Should().Contain(" ");
        item.Progress.Should().BeInRange(0, 1);
        item.SecondsRemaining.Should().BeInRange(0, 30);
    }

    [Fact]
    public void Dashboard_Load_PopulatesAccountsAndRefreshes()
    {
        using var vm = new DashboardViewModel(() => DateTimeOffset.UtcNow, startTimer: false);

        vm.Load([DemoAccount()]);

        vm.Accounts.Should().ContainSingle();
        vm.Accounts[0].Code.Should().HaveLength(6);
    }

    [Fact]
    public void Dashboard_CopyCode_SetsStatus()
    {
        using var vm = new DashboardViewModel(() => DateTimeOffset.UtcNow, startTimer: false);
        vm.Load([DemoAccount()]);

        vm.CopyCodeCommand.Execute(vm.Accounts[0]);

        vm.StatusMessage.Should().Contain("SafeAuthenticator");
    }

    [Fact]
    public void Login_Unlock_RaisesEventWithPassword()
    {
        var vm = new LoginViewModel { MasterPassword = "hunter2" };
        string? captured = null;
        vm.UnlockRequested += (_, pw) => captured = pw;

        vm.UnlockCommand.Execute(null);

        captured.Should().Be("hunter2");
    }

    [Fact]
    public void Login_Unlock_DisabledWhenEmpty()
    {
        var vm = new LoginViewModel { MasterPassword = string.Empty };

        vm.UnlockCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AccountEditor_TryBuild_RejectsInvalidSecret()
    {
        var vm = new AccountEditorViewModel
        {
            Issuer = "Acme",
            Label = "user",
            Secret = "not valid base32 !!!",
        };

        vm.TryBuild(out var account).Should().BeFalse();
        account.Should().BeNull();
        vm.ValidationError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AccountEditor_TryBuild_BuildsValidAccount()
    {
        var vm = new AccountEditorViewModel
        {
            Issuer = "Acme",
            Label = "user",
            Secret = "JBSWY3DPEHPK3PXP",
        };

        vm.TryBuild(out var account).Should().BeTrue();
        account.Should().NotBeNull();
        account!.Issuer.Should().Be("Acme");
    }

    [Fact]
    public void AccountEditor_LoadFrom_CopiesValues()
    {
        var vm = new AccountEditorViewModel();

        vm.LoadFrom(DemoAccount());

        vm.Issuer.Should().Be("SafeAuthenticator");
        vm.Secret.Should().Be("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Import_FromUri_ParsesAccount()
    {
        var vm = new ImportViewModel
        {
            ManualUri = "otpauth://totp/Acme:user@acme.com?secret=JBSWY3DPEHPK3PXP&issuer=Acme",
        };

        vm.ImportFromUri().Should().BeTrue();
        vm.ImportedAccount.Should().NotBeNull();
        vm.ImportedAccount!.Issuer.Should().Be("Acme");
    }

    [Fact]
    public void Import_FromUri_RejectsGarbage()
    {
        var vm = new ImportViewModel { ManualUri = "https://example.com" };

        vm.ImportFromUri().Should().BeFalse();
        vm.ImportedAccount.Should().BeNull();
    }

    [Fact]
    public void Settings_DefaultsAreSensible()
    {
        var vm = new SettingsViewModel();

        vm.Themes.Should().Contain(AppTheme.System);
        vm.AutoLockMinutes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MainWindow_Unlock_NavigatesToDashboard()
    {
        var vm = new MainWindowViewModel();

        vm.UnlockCommand.Execute(null);

        vm.IsUnlocked.Should().BeTrue();
        vm.CurrentPage.Should().Be(vm.Dashboard);
    }

    [Fact]
    public void MainWindow_Lock_ReturnsToLogin()
    {
        var vm = new MainWindowViewModel();
        vm.UnlockCommand.Execute(null);

        vm.LockCommand.Execute(null);

        vm.IsUnlocked.Should().BeFalse();
        vm.CurrentPage.Should().Be(vm.Login);
    }

    [Fact]
    public void Backup_SelectingWebDav_TogglesVisibilityFlags()
    {
        var vm = new BackupViewModel();

        vm.IsLocal.Should().BeTrue();
        vm.IsWebDav.Should().BeFalse();

        vm.SelectedProvider = BackupProviderKind.WebDav;

        vm.IsLocal.Should().BeFalse();
        vm.IsWebDav.Should().BeTrue();
    }

    [Fact]
    public async Task Backup_ExportLocal_WithoutFolder_SetsStatus()
    {
        var vm = new BackupViewModel { SelectedProvider = BackupProviderKind.LocalFolder };

        var ok = await vm.ExportAsync([]);

        ok.Should().BeFalse();
        vm.Status.Should().Contain("folder");
    }

    [Fact]
    public async Task Backup_ExportWebDav_WithoutUrl_SetsStatus()
    {
        var vm = new BackupViewModel { SelectedProvider = BackupProviderKind.WebDav };

        var ok = await vm.ExportAsync([]);

        ok.Should().BeFalse();
        vm.Status.Should().Contain("WebDAV");
    }

    [Fact]
    public async Task Backup_LocalRoundTrip_ExportsAndImports()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sa-vm-{Guid.NewGuid():N}");
        try
        {
            var vm = new BackupViewModel
            {
                SelectedProvider = BackupProviderKind.LocalFolder,
                Folder = folder,
                MasterPassword = "pw",
            };

            (await vm.ExportAsync([DemoAccount()])).Should().BeTrue();
            vm.Backups.Should().ContainSingle();

            vm.SelectedBackup = vm.Backups[0];
            var restored = await vm.ImportAsync();

            restored.Should().NotBeNull();
            restored!.Should().ContainSingle();
            restored![0].Secret.Should().Be("JBSWY3DPEHPK3PXP");
        }
        finally
        {
            if (System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.Delete(folder, recursive: true);
            }
        }
    }
}
