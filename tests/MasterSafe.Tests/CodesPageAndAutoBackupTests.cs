// File:    CodesPageAndAutoBackupTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public sealed class CodesPageAndAutoBackupTests : IDisposable
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
        Passwords = [new PasswordEntry { Title = "Mail", UserName = "a@b.c", Password = "p@ssw0rd!" }],
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms-codes-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly AppServices _services;
    private readonly FakeDialogs _dialogs = new();
    private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public CodesPageAndAutoBackupTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(_paths.DataFolder);
        _settings = new SettingsStore(_paths.SettingsPath);
        var repository = new SwitchableVaultRepository(_paths.DefaultVaultPath);
        var catalog = new VaultCatalog(_paths, repository, _settings);
        var encryption = new AesGcmEncryptionService();
        var passwords = new MasterPasswordService();
        var hybrid = PostQuantumProviderFactory.CreateHybrid();
        _services = new AppServices(
            _paths, catalog, _settings, _dialogs,
            new RecoveryKeyService(encryption, passwords, hybrid, FastKdf),
            new RecipientBackupService(encryption, hybrid));
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(_root); attempt++)
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed class FakeDialogs : IFileDialogs
    {
        public string? FolderResult { get; set; }

        public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderResult);

        public void ShowInFolder(string path)
        {
        }
    }

    private sealed class FakeTimer(TimeSpan after, Func<Task> action) : IDisposable
    {
        public TimeSpan After { get; } = after;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        public Task FireAsync() => action();
    }

    // ------------------------------------------------------------------ the codes page

    private static OtpAccount Account(string issuer, string label = "me@example.com", string secret = "JBSWY3DPEHPK3PXP")
        => new() { Issuer = issuer, Label = label, Secret = secret };

    private static DashboardViewModel Dashboard(params OtpAccount[] accounts)
    {
        var vm = new DashboardViewModel(startTimer: false);
        vm.Load(accounts);
        return vm;
    }

    [Fact]
    public void SearchFindsByIssuerOrLabel_AndSaysWhenNothingMatches()
    {
        var vm = Dashboard(Account("GitHub", "alice"), Account("Amazon", "bob"), Account("Google", "carol"), Account("Steam", "dave"));

        vm.SearchText = "go";
        vm.Shown.Select(a => a.Issuer).Should().Equal("Google");

        vm.SearchText = "BOB";
        vm.Shown.Select(a => a.Issuer).Should().Equal("Amazon");

        vm.SearchText = "zzz";
        vm.Shown.Should().BeEmpty();
        vm.HasNoMatches.Should().BeTrue();

        vm.SearchText = string.Empty;
        vm.Shown.Should().HaveCount(4);
        vm.HasNoMatches.Should().BeFalse();
    }

    [Fact]
    public void TheSearchBoxOnlyAppearsWhenThereAreEnoughAccounts()
    {
        Dashboard(Account("A"), Account("B"), Account("C")).ShowSearch.Should().BeFalse();
        Dashboard(Account("A"), Account("B"), Account("C"), Account("D")).ShowSearch.Should().BeTrue();
    }

    [Fact]
    public void TheAccountsCanBeOrderedByName()
    {
        var vm = Dashboard(Account("Mail"), Account("Amazon"), Account("Zoom"));

        vm.Shown.Select(a => a.Issuer).Should().Equal("Mail", "Amazon", "Zoom");

        vm.SelectedSort = DashboardViewModel.SortOptions.Single(o => o.Key == "name");
        vm.Shown.Select(a => a.Issuer).Should().Equal("Amazon", "Mail", "Zoom");

        vm.SelectedSort = DashboardViewModel.SortOptions.Single(o => o.Key == "name-desc");
        vm.Shown.Select(a => a.Issuer).Should().Equal("Zoom", "Mail", "Amazon");
    }

    [Fact]
    public void TheOrderIsAnnouncedWhenChosen_NotWhenRestored()
    {
        var vm = Dashboard(Account("B"), Account("A"));
        var raised = 0;
        vm.SortChanged += (_, _) => raised++;

        vm.SelectedSort = DashboardViewModel.SortOptions[1];
        vm.RestoreSort("name-desc");

        raised.Should().Be(1);
        vm.SelectedSort.Key.Should().Be("name-desc");
    }

    [Fact]
    public void RenamingAnAccount_ChangesOnlyItsNames()
    {
        var vm = Dashboard(Account("Github", "old"));
        var original = vm.Accounts[0].Account;
        var saves = 0;
        vm.AccountsChanged += (_, _) => saves++;

        vm.BeginEditCommand.Execute(vm.Accounts[0]);
        vm.IsEditing.Should().BeTrue();
        vm.EditIssuer.Should().Be("Github");
        vm.EditIssuer = "  GitHub ";
        vm.EditLabel = "alice@example.com";
        vm.SaveEditCommand.Execute(null);

        var renamed = vm.Accounts.Should().ContainSingle().Subject.Account;
        (renamed.Issuer, renamed.Label).Should().Be(("GitHub", "alice@example.com"));
        renamed.Id.Should().Be(original.Id);
        renamed.Secret.Should().Be(original.Secret);
        renamed.Digits.Should().Be(original.Digits);
        renamed.Period.Should().Be(original.Period);
        vm.IsEditing.Should().BeFalse();
        vm.Shown.Single().Issuer.Should().Be("GitHub");
        saves.Should().Be(1);
    }

    [Fact]
    public void RenamingToNothing_IsRefused_AndCancelChangesNothing()
    {
        var vm = Dashboard(Account("GitHub", "a"));
        vm.BeginEditCommand.Execute(vm.Accounts[0]);
        vm.EditIssuer = string.Empty;
        vm.EditLabel = "  ";

        vm.SaveEditCommand.Execute(null);

        vm.StatusMessage.Should().Contain("Type an issuer");
        vm.IsEditing.Should().BeTrue();

        vm.CancelEditCommand.Execute(null);
        vm.IsEditing.Should().BeFalse();
        vm.Accounts[0].Account.Issuer.Should().Be("GitHub");
    }

    [Fact]
    public void RemindersCanBeDismissed_UntilTheVaultIsLocked()
    {
        var vm = Dashboard();
        var command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        var a = new NoticeRow("a", "First", "Do", command);
        var b = new NoticeRow("b", "Second", "Do", command);

        vm.SetNotices([a, b]);
        vm.DismissNoticeCommand.Execute(a);
        vm.Notices.Select(n => n.Key).Should().Equal("b");

        vm.SetNotices([a, b]);
        vm.Notices.Select(n => n.Key).Should().Equal("b");

        vm.ResetNotices();
        vm.SetNotices([a, b]);
        vm.Notices.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------ reminders in the shell

    private async Task<MainWindowViewModel> UnlockedShellAsync()
    {
        var vault = new VaultService(
            new SwitchableVaultRepository(_paths.DefaultVaultPath), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);
        await vault.CreateAsync("a long test master password");
        var shell = new MainWindowViewModel(vault, services: _services, clock: () => _now);
        await shell.UnlockVaultAsync("a long test master password");
        shell.IsUnlocked.Should().BeTrue();
        return shell;
    }

    private void GiveRecoveryKey(DateTimeOffset? lastBackup)
    {
        var created = _services.RecoveryKeys.Create("the recovery key passphrase");
        var settings = _settings.Load();
        settings.RecoveryPublicKey = Convert.ToBase64String(created.PublicKey);
        settings.RecoveryKeyId = created.KeyId;
        settings.RecoveryKeyCreatedUtc = created.CreatedUtc;
        settings.LastRecoveryBackupUtc = lastBackup?.ToString("O");
        _settings.Save(settings);
    }

    [Fact]
    public async Task WithoutARecoveryKey_TheCodesPageSaysSo()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var shell = await UnlockedShellAsync();
        shell.CurrentPage = shell.Dashboard;

        shell.Dashboard.Notices.Select(n => n.Key).Should().Contain("recovery-key");
    }

    [Fact]
    public async Task AnOldOrMissingBackup_IsReported_AFreshOneIsNot()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        GiveRecoveryKey(_now.AddDays(-45));
        var shell = await UnlockedShellAsync();
        shell.CurrentPage = shell.Dashboard;
        shell.Dashboard.Notices.Single(n => n.Key == "recovery-backup").Text.Should().Contain("45 days old");
        shell.Dashboard.Notices.Select(n => n.Key).Should().NotContain("recovery-key");

        var settings = _settings.Load();
        settings.LastRecoveryBackupUtc = _now.AddDays(-2).ToString("O");
        _settings.Save(settings);
        shell.Recovery.Refresh();
        shell.RefreshNotices();

        shell.Dashboard.Notices.Select(n => n.Key).Should().NotContain("recovery-backup");
    }

    [Fact]
    public async Task RemindersCanBeSwitchedOff_AndTheUpdateOneTurnsTheSettingOn()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var shell = await UnlockedShellAsync();
        shell.RefreshNotices();

        var updates = shell.Dashboard.Notices.Single(n => n.Key == "updates");
        updates.Action.Execute(null);
        shell.Settings.CheckForUpdatesOnStart.Should().BeTrue();
        shell.Dashboard.Notices.Select(n => n.Key).Should().NotContain("updates");

        shell.Settings.ShowReminders = false;
        shell.RefreshNotices();
        shell.Dashboard.Notices.Should().BeEmpty();
    }

    [Fact]
    public async Task LockingTheVault_ClearsTheReminders()
    {
        var shell = await UnlockedShellAsync();
        shell.RefreshNotices();
        shell.Dashboard.Notices.Should().NotBeEmpty();

        shell.LockNow();

        shell.Dashboard.Notices.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ automatic recovery backups

    [Fact]
    public void OnlyTheAppsOwnBackupFiles_AreEverPruned_AndTheNewestSurvive()
    {
        var folder = Path.Combine(_root, "backups");
        Directory.CreateDirectory(folder);
        var mine = Enumerable.Range(1, 8).Select(i => $"recovery-2026110{i}-120000.sauth").ToList();
        var others = new[] { "recovery-notes.sauth", "recovery-2026-11-01.sauth", "holiday.jpg", "recovery-20261101-120000.sauth.bak", "vault.sauth" };
        foreach (var name in mine.Concat(others))
        {
            File.WriteAllText(Path.Combine(folder, name), "x");
        }

        var deleted = AutoRecoveryBackup.Prune(folder);

        deleted.Should().Be(3);
        Directory.GetFiles(folder).Select(Path.GetFileName).Should().BeEquivalentTo(mine.Skip(3).Concat(others));
    }

    [Fact]
    public void PruningAnUnknownFolderOrZeroKeep_DoesNothing()
    {
        AutoRecoveryBackup.Prune(Path.Combine(_root, "nope")).Should().Be(0);
        var folder = Path.Combine(_root, "keepzero");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "recovery-20261101-120000.sauth"), "x");

        AutoRecoveryBackup.Prune(folder, keep: 0).Should().Be(0);

        Directory.GetFiles(folder).Should().ContainSingle();
    }

    [Theory]
    [InlineData("recovery-20261101-143015.sauth", true)]
    [InlineData("recovery-20261101-14301.sauth", false)]
    [InlineData("recovery-20261101-143015.sauth.sig", false)]
    [InlineData("..\\recovery-20261101-143015.sauth", false)]
    [InlineData("Recovery-20261101-143015.sauth", false)]
    public void BackupNames_MustMatchExactly(string name, bool expected)
        => AutoRecoveryBackup.IsBackupName(name).Should().Be(expected);

    [Fact]
    public async Task ADebouncedAction_RunsOnceAfterTheLastRequest()
    {
        var timers = new List<FakeTimer>();
        var runs = 0;
        var debounce = new DebouncedAction((after, action) =>
        {
            var t = new FakeTimer(after, action);
            timers.Add(t);
            return t;
        }, TimeSpan.FromSeconds(60), () =>
        {
            runs++;
            return Task.CompletedTask;
        });

        debounce.Trigger();
        debounce.Trigger();
        debounce.Trigger();

        timers.Should().HaveCount(3);
        timers.Take(2).Should().OnlyContain(t => t.Disposed);
        await timers[0].FireAsync();
        runs.Should().Be(0, "an old timer that still ticks does nothing");
        await timers[2].FireAsync();
        runs.Should().Be(1);
        debounce.IsPending.Should().BeFalse();
    }

    private RecoveryViewModel Recovery(string folder, List<FakeTimer>? timers = null, bool unlocked = true, VaultContents? contents = null)
    {
        GiveRecoveryKey(null);
        var vm = new RecoveryViewModel(_services, () => contents ?? Sample)
        {
            Clock = () => _now,
            IsVaultUnlocked = () => unlocked,
            TimerFactory = (after, action) =>
            {
                var t = new FakeTimer(after, action);
                timers?.Add(t);
                return t;
            },
        };
        _dialogs.FolderResult = folder;
        return vm;
    }

    [Fact]
    public async Task SwitchingItOn_NeedsAFolder_AndThePickerDoesIt()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(_root, "auto");
        var vm = Recovery(folder);

        vm.AutoBackupEnabled = true;
        await Task.Delay(100);

        vm.AutoBackupFolder.Should().Be(folder);
        vm.AutoBackupEnabled.Should().BeTrue();
        var saved = _settings.Load();
        saved.AutoRecoveryBackupEnabled.Should().BeTrue();
        saved.AutoRecoveryBackupFolder.Should().Be(folder);
    }

    [Fact]
    public async Task AChangeStartsAMinuteOfWaiting_ThenABackupIsWritten_AndRecorded()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(_root, "auto2");
        var timers = new List<FakeTimer>();
        var vm = Recovery(folder, timers);
        await vm.ChooseAutoBackupFolderCommand.ExecuteAsync(null);

        vm.NotifyVaultChanged();

        timers.Should().ContainSingle().Which.After.Should().Be(TimeSpan.FromSeconds(60));
        await timers[0].FireAsync();

        var files = Directory.GetFiles(folder);
        files.Should().ContainSingle().Which.Should().MatchRegex(@"recovery-\d{8}-\d{6}\.sauth$");
        new FileInfo(files[0]).Length.Should().BeGreaterThan(100);
        vm.AutoBackupStatus.Should().Contain("Automatic recovery backup saved");
        _settings.Load().LastRecoveryBackupUtc.Should().NotBeNullOrEmpty();
        File.ReadAllText(files[0]).Should().NotContain("p@ssw0rd!").And.NotContain("GitHub");
    }

    [Fact]
    public async Task ABackupIsNotWrittenWhileLocked_OrForAnEmptyVault_OrTooSoonAfterTheLast()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(_root, "auto3");
        var locked = Recovery(folder, unlocked: false);
        await locked.ChooseAutoBackupFolderCommand.ExecuteAsync(null);
        await locked.RunAutoBackupAsync();
        (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).Should().BeEmpty("a locked vault is never backed up");

        var empty = Recovery(folder, contents: new VaultContents());
        await empty.ChooseAutoBackupFolderCommand.ExecuteAsync(null);
        await empty.RunAutoBackupAsync();
        (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).Should().BeEmpty("an empty vault must never replace real backups");

        var timers = new List<FakeTimer>();
        var normal = Recovery(folder, timers);
        await normal.ChooseAutoBackupFolderCommand.ExecuteAsync(null);
        normal.NotifyVaultChanged();
        await normal.RunAutoBackupAsync();
        Directory.GetFiles(folder).Should().ContainSingle();

        await normal.RunAutoBackupAsync();
        Directory.GetFiles(folder).Should().ContainSingle("a second run within five minutes waits");
        timers.Should().HaveCountGreaterThan(1, "it was scheduled again for later");
    }

    [Fact]
    public async Task OldBackupsAreRemoved_ButTheLatestFiveStay()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(_root, "auto4");
        Directory.CreateDirectory(folder);
        for (var i = 1; i <= 6; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"recovery-2025010{i}-120000.sauth"), "old");
        }

        var vm = Recovery(folder);
        await vm.ChooseAutoBackupFolderCommand.ExecuteAsync(null);
        await vm.RunAutoBackupAsync();

        var names = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n).ToList();
        names.Should().HaveCount(AutoRecoveryBackup.KeepNewest);
        names.Last().Should().NotStartWith("recovery-2025", "the new backup is among the five that stay");
    }

    [Fact]
    public async Task SwitchingItOff_CancelsAWaitingBackup()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var folder = Path.Combine(_root, "auto5");
        var timers = new List<FakeTimer>();
        var vm = Recovery(folder, timers);
        await vm.ChooseAutoBackupFolderCommand.ExecuteAsync(null);
        vm.NotifyVaultChanged();

        vm.AutoBackupEnabled = false;
        await timers[0].FireAsync();

        timers[0].Disposed.Should().BeTrue();
        (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).Should().BeEmpty();
        _settings.Load().AutoRecoveryBackupEnabled.Should().BeFalse();
    }

    [Fact]
    public void WithoutARecoveryKeyOrWhenOff_NothingIsScheduled()
    {
        var timers = new List<FakeTimer>();
        var vm = new RecoveryViewModel(_services, () => Sample)
        {
            TimerFactory = (after, action) =>
            {
                var t = new FakeTimer(after, action);
                timers.Add(t);
                return t;
            },
        };

        vm.NotifyVaultChanged();

        timers.Should().BeEmpty();
    }
}
