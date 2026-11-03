// File:    Release16Tests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.App.Views;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Core.Import;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using SkiaSharp;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>Tests for the 1.6 foundations: older vaults, one running copy, settings in the vault, older copies, and more.</summary>
public sealed class Release16Tests : IDisposable
{
    private static readonly Argon2Parameters FastKdf = new() { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms-16-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly AppServices _services;

    public Release16Tests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(_paths.DataFolder);
        _settings = new SettingsStore(_paths.SettingsPath);
        _services = NewServices();
    }

    private AppServices NewServices()
    {
        var repository = new SwitchableVaultRepository(_paths.DefaultVaultPath);
        var encryption = new AesGcmEncryptionService();
        var hybrid = PostQuantumProviderFactory.CreateHybrid();
        return new AppServices(
            _paths, new VaultCatalog(_paths, repository, _settings), _settings, new NoDialogs(),
            new RecoveryKeyService(encryption, new MasterPasswordService(), hybrid, FastKdf),
            new RecipientBackupService(encryption, hybrid));
    }

    public void Dispose()
    {
        AutoHideReveal.HideOnFocusLoss = true;
        ClipboardPolicy.Use(ClipboardPolicy.DefaultSeconds);
        Loc.Use("en");
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public void ShowInFolder(string path)
        {
        }
    }

    private VaultService NewVault() => NewVault(_paths.DefaultVaultPath);

    private static VaultService NewVault(string path) => new(new FileVaultRepository(path), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    private MainWindowViewModel NewShell() => new(new VaultService(new SwitchableVaultRepository(_paths.DefaultVaultPath), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf), services: NewServices());

    private const string Password = "correct horse battery staple";

    // ---------------------------------------------------------------- vaults from 1.5.1

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "vault-1.5.1.sauth");

    [Fact]
    public async Task AVaultWrittenBy151_Opens_AndNothingIsLost()
    {
        var copy = Path.Combine(Directory.CreateDirectory(_root).FullName, "old.sauth");
        File.Copy(Fixture, copy);
        var vault = NewVault(copy);

        var key = await vault.DeriveKeyAsync("fixture-password-151");
        var contents = await vault.OpenWithKeyAsync(key);

        contents.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("GitHub");
        contents.Passwords.Should().HaveCount(2);
        var entry = contents.Passwords.Single(p => p.Title == "GitHub");
        entry.Password.Should().Be("correct horse battery");
        entry.Notes.Should().Be("my note");
        entry.Folder.Should().Be("Work/Code");
        entry.History.Should().ContainSingle(v => v.Password == "old-password");
        contents.AttachmentData[entry.Attachments[0].Id].Should().Equal("hello"u8.ToArray());
        contents.Folders.Should().Contain("Empty");
        contents.FolderIcons["Work/Code"].Should().Be("server");
        contents.RecoveryKeyFingerprint.Should().Be("abc123");
        contents.Security.Should().BeNull("1.5.1 had none");

        await vault.SaveWithKeyAsync(key, contents with { Security = new VaultSecurity(), SaveCounter = 1 });
        var again = await vault.OpenWithKeyAsync(key);
        again.Passwords.Should().HaveCount(2);
        again.AttachmentData.Should().ContainSingle();
    }

    [Fact]
    public void WhatAnOlderVersionDropsOnSave_ComesBackFromTheNotes()
    {
        var card = new PasswordEntry
        {
            Title = "Visa",
            Password = string.Empty,
            Notes = "for travel",
            Kind = EntryKind.Card,
            Fields = [new CustomField { Name = "Card number", Value = "4111 1111", IsSecret = true }],
            ExpiresUtc = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
        };

        var written = VaultContentsCodec.Encode(new VaultContents { Passwords = [card] });

        // An older version knows only the old properties, so these are gone after it saves.
        var json = JsonNodeWithout(written, "Kind", "Fields", "ExpiresUtc", "IconPng", "StateChangedUtc");
        var read = VaultContentsCodec.Decode(json).Passwords.Single();

        read.Kind.Should().Be(EntryKind.Card);
        read.Fields.Should().ContainSingle(f => f.Value == "4111 1111" && f.IsSecret);
        read.Notes.Should().Be("for travel");
        read.ExpiresUtc.Should().Be(card.ExpiresUtc);
    }

    private static byte[] JsonNodeWithout(byte[] json, params string[] names)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        foreach (var entry in root["Passwords"]!.AsArray())
        {
            foreach (var name in names)
            {
                entry!.AsObject().Remove(name);
            }
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    [Fact]
    public void AnEditedNotesBlock_StaysInTheNotes_InsteadOfBeingLost()
    {
        var entry = new PasswordEntry { Title = "x", Password = "p", Notes = $"{EntryExtras.Marker}\nnot json at all" };

        EntryExtras.Detach(entry).Notes.Should().Contain("not json at all");
    }

    [Fact]
    public void AnOrdinaryLogin_GetsNoBlock_SoItsNotesStayExactlyTheSame()
        => EntryExtras.Attach(new PasswordEntry { Title = "x", Password = "p", Notes = "plain" }).Notes.Should().Be("plain");

    // ---------------------------------------------------------------- one running copy

    [Fact]
    public void TheDataFolder_DoesNotDependOnWhereTheProgramWasStarted()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AppPaths.Default.DataFolder.Should().StartWith(expected).And.EndWith("MasterSafe");
        AppPaths.Default.StateFolder.Should().StartWith(expected).And.EndWith("MasterSafe");
        AppPaths.Default.StateFolder.Should().NotBe(AppPaths.Default.DataFolder, "the copy of the counter is kept away from the vault");
    }

    [Fact]
    public void ASecondCopy_CannotTakeTheLock_UntilTheFirstEnds()
    {
        var folder = Path.Combine(_root, "lock");
        using (var first = SingleInstance.TryAcquire(folder))
        {
            first.Should().NotBeNull();
            SingleInstance.TryAcquire(folder).Should().BeNull();
        }

        using var later = SingleInstance.TryAcquire(folder);
        later.Should().NotBeNull("the lock goes away with the program that held it, even after a crash");
    }

    [Fact]
    public async Task ASecondCopy_BringsTheFirstForward()
    {
        var folder = Path.Combine(_root, "signal");
        using var first = SingleInstance.TryAcquire(folder)!;
        var shown = new TaskCompletionSource();
        first.Listen(() => shown.TrySetResult());

        SingleInstance.SignalRunningCopy(folder).Should().BeTrue();

        await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhenNothingAnswers_TheNewStartSaysSo()
        => SingleInstance.SignalRunningCopy(Path.Combine(_root, "nobody"), TimeSpan.FromMilliseconds(100)).Should().BeFalse();

    // ---------------------------------------------------------------- security settings

    [Fact]
    public async Task EditingTheSettingsFile_CannotTurnOffProtection_OnceTheVaultIsOpen()
    {
        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);
        shell.Settings.AutoLockMinutes = 3;
        shell.Settings.LockOnMinimize = true;
        shell.Settings.BlockScreenCapture = true;
        shell.Settings.HideCodes = true;
        await shell.PersistAsync();
        shell.LockNow();

        var tampered = _settings.Load();
        tampered.AutoLockMinutes = 120;
        tampered.LockOnSessionLock = false;
        tampered.LockOnMinimize = false;
        tampered.BlockScreenCapture = false;
        tampered.BreachCheckEnabled = true;
        tampered.ClipboardClearSeconds = 120;
        tampered.HideCodes = false;
        tampered.HideRevealedOnFocusLoss = false;
        _settings.Save(tampered);

        var again = NewShell();
        await again.UnlockVaultAsync(Password);

        again.Settings.AutoLockMinutes.Should().Be(3);
        again.Settings.LockOnSessionLock.Should().BeTrue();
        again.Settings.LockOnMinimize.Should().BeTrue();
        again.Settings.BlockScreenCapture.Should().BeTrue();
        again.Settings.BreachCheckEnabled.Should().BeFalse();
        again.Settings.HideCodes.Should().BeTrue();
        again.Settings.HideRevealedOnFocusLoss.Should().BeTrue();
        again.Dashboard.HideCodes.Should().BeTrue();
    }

    [Fact]
    public async Task AVaultFromBefore16_TakesItsValuesFromTheSettingsFile_Once()
    {
        await NewVault().SaveContentsAsync(Password, new VaultContents());
        var settings = _settings.Load();
        settings.AutoLockMinutes = 7;
        _settings.Save(settings);

        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);

        var key = await NewVault().DeriveKeyAsync(Password);
        (await NewVault().OpenWithKeyAsync(key)).Security!.AutoLockMinutes.Should().Be(7);
    }

    [Fact]
    public async Task ChangingASetting_IsSavedInTheVaultAtOnce()
    {
        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);

        shell.Settings.AutoLockMinutes = 9;

        // The save runs in the background, and an empty save behind it waits for its turn.
        await shell.PersistAsync();
        var key = await NewVault().DeriveKeyAsync(Password);
        (await NewVault().OpenWithKeyAsync(key)).Security!.AutoLockMinutes.Should().Be(9);
    }

    [Fact]
    public void AStrangeValueInTheVault_IsBroughtIntoRange()
    {
        var settings = new SettingsViewModel();
        settings.ApplySecurity(new VaultSecurity { AutoLockMinutes = 100000, ClipboardClearSeconds = 99999 });

        settings.AutoLockMinutes.Should().Be(120);
        settings.SelectedClipboardDelay.Seconds.Should().Be(ClipboardPolicy.DefaultSeconds);
    }

    // ---------------------------------------------------------------- network paths

    [Theory]
    [InlineData(@"\\evil.example\share\vault.sauth")]
    [InlineData(@"//evil.example/share/vault.sauth")]
    [InlineData(@"\\?\UNC\evil.example\share\vault.sauth")]
    [InlineData(@"\\10.0.0.5\c$\vault.sauth")]
    public void NetworkPaths_AreRecognisedWithoutTouchingTheNetwork(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            VaultPathSafety.IsNetworkPath(path).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(@"C:\Users\me\vault.sauth")]
    [InlineData(@"\\?\C:\vault.sauth")]
    [InlineData("")]
    [InlineData(null)]
    public void LocalPaths_AreNotNetworkPaths(string? path) => VaultPathSafety.IsNetworkPath(path).Should().BeFalse();

    [Fact]
    public async Task ANetworkPathInTheSettingsFile_IsNotOpened_AndTheUserDecides()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string hostile = @"\\evil.invalid\share\vault.sauth";
        var settings = _settings.Load();
        settings.ActiveVaultPath = hostile;
        settings.KnownVaultPaths = [hostile];
        _settings.Save(settings);

        var services = NewServices();
        services.Catalog.ActivePath.Should().Be(_paths.DefaultVaultPath);
        services.Catalog.RefusedNetworkPath.Should().Be(hostile);
        services.Catalog.List().Should().NotContain(v => v.Path == hostile, "not even listing may touch the share");
        var refuse = () => services.Catalog.SetActive(hostile);
        refuse.Should().Throw<NetworkVaultException>();

        var shell = new MainWindowViewModel(NewVault(), services: services);
        shell.Login.HasRefusedPath.Should().BeTrue();
        shell.Login.RefusedPathText.Should().Contain(hostile);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AVaultPickedOnAShare_AsksFirst()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var page = new VaultsViewModel(_services);
        var row = new VaultRowViewModel(new VaultEntry(@"\\evil.invalid\share\v.sauth", "v.sauth", VaultKind.External, DateTimeOffset.UtcNow, 1, false));

        await page.OpenRowCommand.ExecuteAsync(row);

        page.IsConfirmingNetwork.Should().BeTrue();
        page.NetworkQuestion.Should().Contain("network share");
    }

    // ---------------------------------------------------------------- older copy

    [Fact]
    public async Task AnOlderCopyOfTheVault_IsNoticed_AndStillOpens()
    {
        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);
        shell.Dashboard.Add(new OtpAccount { Issuer = "One", Secret = "JBSWY3DPEHPK3PXP" });
        await shell.PersistAsync();
        var older = File.ReadAllBytes(_paths.DefaultVaultPath);
        shell.Dashboard.Add(new OtpAccount { Issuer = "Two", Secret = "JBSWY3DPEHPK3PXP" });
        await shell.PersistAsync();
        await LockAndSettleAsync(shell);

        File.WriteAllBytes(_paths.DefaultVaultPath, older);

        var again = NewShell();
        await again.UnlockVaultAsync(Password);

        again.IsUnlocked.Should().BeTrue("restoring a backup is normal, so this never blocks");
        again.Dashboard.Accounts.Should().ContainSingle();
        again.RefreshNotices();
        again.Dashboard.Notices.Should().Contain(n => n.Key == "older-copy");
    }

    [Fact]
    public async Task TheNotice_AppearsEvenWithRemindersTurnedOff_AndGoesAwayWhenAccepted()
    {
        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);
        await shell.PersistAsync();
        var older = File.ReadAllBytes(_paths.DefaultVaultPath);
        await shell.PersistAsync();
        await LockAndSettleAsync(shell);
        File.WriteAllBytes(_paths.DefaultVaultPath, older);

        var again = NewShell();
        await again.UnlockVaultAsync(Password);
        again.Settings.ShowReminders = false;
        again.RefreshNotices();
        var notice = again.Dashboard.Notices.Single(n => n.Key == "older-copy");

        notice.Action.Execute(null);
        await again.PersistAsync();

        again.Dashboard.Notices.Should().NotContain(n => n.Key == "older-copy");
        again.LockNow();
        var third = NewShell();
        await third.UnlockVaultAsync(Password);
        third.RefreshNotices();
        third.Dashboard.Notices.Should().NotContain(n => n.Key == "older-copy", "accepted once is enough");
    }

    // Locking refreshes the unlock methods in the background, which reads the vault file for a moment.
    private static async Task LockAndSettleAsync(MainWindowViewModel shell)
    {
        shell.LockNow();
        await shell.RefreshUnlockMethodsAsync();
    }

    [Fact]
    public void ACounterNoVaultCouldReach_IsTreatedAsUnknown()
    {
        var store = new SaveCounterStore(_paths.SaveCounterPath);

        store.Set("id", long.MaxValue);
        store.Get("id").Should().Be(0, "adding one to this would wrap around and warn for ever");
        store.Set("id", -5);
        store.Get("id").Should().Be(0);
    }

    /// <summary>A folder whose downloads wait until the test lets them go on.</summary>
    private sealed class HeldProvider(IBackupProvider inner) : IBackupProvider
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => inner.Name;

        public Task<string> UploadAsync(string fileName, byte[] encryptedContent, CancellationToken cancellationToken = default)
            => inner.UploadAsync(fileName, encryptedContent, cancellationToken);

        public async Task<byte[]> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task;
            return await inner.DownloadAsync(fileName, cancellationToken);
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    }

    [Fact]
    public async Task ASyncFinishingAfterTheVaultLocked_PutsNothingBackOnTheScreens()
    {
        var folder = Path.Combine(_root, "sync");
        var key = SyncCode.NewKey();
        var other = new VaultContents
        {
            Passwords = [new PasswordEntry { Title = "From elsewhere", Password = "secret" }],
            Sync = new SyncSettings { Folder = folder, Key = Convert.ToBase64String(key) },
        };
        await new SyncService(new AesGcmEncryptionService()).RunAsync(other, new LocalFolderBackupProvider(folder));

        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);
        var held = new HeldProvider(new LocalFolderBackupProvider(folder));
        shell.SyncProviderFactory = _ => held;
        shell.Sync.Folder = folder;
        shell.Sync.JoinCode = SyncCode.Format(key);

        var join = shell.Sync.JoinCommand.ExecuteAsync(null);
        await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        shell.LockNow();
        held.Release.SetResult();
        await join;

        shell.Passwords.Snapshot().Should().BeEmpty("a locked vault holds nothing in memory");
        shell.Sync.Status.Should().Contain("locked");
        shell.Sync.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task SyncThroughTheShell_JoinsSavesAndKeepsItsSettingsInTheVault()
    {
        var folder = Path.Combine(_root, "sync2");
        var first = NewShell();
        await first.UnlockVaultAsync(Password);
        first.Passwords.AddRange([new PasswordEntry { Title = "One", Password = "secret-one" }]);
        first.Sync.Folder = folder;
        await first.Sync.StartCommand.ExecuteAsync(null);

        first.Sync.IsConfigured.Should().BeTrue();
        first.Sync.IsCodeShown.Should().BeTrue("the second computer needs it");
        var code = first.Sync.ShownCode!;
        first.CurrentPage = first.Dashboard;
        first.Sync.IsCodeShown.Should().BeFalse("a key does not stay on the screen");

        var key = await NewVault().DeriveKeyAsync(Password);
        var stored = await NewVault().OpenWithKeyAsync(key);
        stored.Sync!.Key.Should().NotBeEmpty();
        File.ReadAllText(_paths.SettingsPath).Should().NotContain(stored.Sync.Key, "the key is never in the settings file");
        Directory.GetFiles(folder).Should().ContainSingle();

        // A second computer: its own vault, with its own password.
        var other = Path.Combine(_root, "second.sauth");
        var second = new MainWindowViewModel(new VaultService(new FileVaultRepository(other), new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf));
        await second.UnlockVaultAsync("another master password here");
        second.Sync.Folder = folder;

        second.Sync.JoinCode = code[..^1] + (code[^1] == 'A' ? 'B' : 'A');
        await second.Sync.JoinCommand.ExecuteAsync(null);
        second.Sync.IsConfigured.Should().BeFalse("a mistyped code is noticed");

        second.Sync.JoinCode = SyncCode.Format(SyncCode.NewKey());
        await second.Sync.JoinCommand.ExecuteAsync(null);
        second.Sync.IsConfigured.Should().BeFalse();
        second.Sync.Status.Should().Contain("not made with this sync code");

        second.Sync.JoinCode = code;
        await second.Sync.JoinCommand.ExecuteAsync(null);

        second.Sync.IsConfigured.Should().BeTrue();
        second.Passwords.Snapshot().Should().ContainSingle().Which.Password.Should().Be("secret-one");
    }

    [Fact]
    public async Task SyncWaits_WhileAnEntryIsBeingTyped()
    {
        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);
        shell.Sync.Folder = Path.Combine(_root, "sync3");
        shell.Passwords.Title = "half typed";

        await shell.Sync.StartCommand.ExecuteAsync(null);

        shell.Sync.IsConfigured.Should().BeFalse();
        shell.Passwords.Title.Should().Be("half typed");
    }

    [Fact]
    public async Task ImportingABackup_MergesIntoTheVault_AndReplacesNothing()
    {
        var folder = Path.Combine(_root, "backups");
        var shell = NewShell();
        await shell.UnlockVaultAsync(Password);
        shell.Passwords.AddRange([new PasswordEntry { Title = "In the backup", Password = "secret-one" }]);
        shell.Backup.Folder = folder;
        shell.Backup.MasterPassword = "short";
        await shell.Backup.ExportCommand.ExecuteAsync(null);
        shell.Backup.Status.Should().Contain("at least 12");

        shell.Backup.MasterPassword = "a long backup password";
        await shell.Backup.ExportCommand.ExecuteAsync(null);
        shell.Backup.Backups.Should().ContainSingle();

        shell.Passwords.ClearFormCommand.Execute(null);
        shell.Passwords.AddRange([new PasswordEntry { Title = "Added later", Password = "secret-two" }]);
        shell.Passwords.DeleteCommand.Execute(shell.Passwords.Snapshot().Single(p => p.Title == "In the backup"));
        shell.Backup.SelectedBackup = shell.Backup.Backups[0];

        shell.Backup.MasterPassword = "the wrong password here";
        await shell.Backup.ImportCommand.ExecuteAsync(null);
        shell.Backup.Status.Should().Contain("does not open");

        shell.Backup.MasterPassword = "a long backup password";
        await shell.Backup.ImportCommand.ExecuteAsync(null);

        shell.Passwords.Snapshot().Select(p => p.Title).Should().BeEquivalentTo("In the backup", "Added later");
    }

    [Fact]
    public async Task NormalUse_NeverWarns()
    {
        for (var i = 0; i < 3; i++)
        {
            var shell = NewShell();
            await shell.UnlockVaultAsync(Password);
            await shell.PersistAsync();
            shell.RefreshNotices();
            shell.Dashboard.Notices.Should().NotContain(n => n.Key == "older-copy");
            shell.LockNow();
        }
    }

    [Fact]
    public void ADamagedCounterFile_IsHarmless()
    {
        Directory.CreateDirectory(_paths.StateFolder);
        File.WriteAllText(_paths.SaveCounterPath, "{ not json");

        var store = new SaveCounterStore(_paths.SaveCounterPath);

        store.Get("id").Should().Be(0);
        store.Set("id", 5);
        store.Get("id").Should().Be(5);
    }

    // ---------------------------------------------------------------- the screens

    [Fact]
    public void LetterIcons_AreStable_AndFollowTheName()
    {
        LetterIcon.Letter("github").Should().Be("G");
        LetterIcon.Letter("  (2) Bank").Should().Be("2");
        LetterIcon.Letter("ñandú").Should().Be("Ñ");
        LetterIcon.Letter("😀").Should().Be("?");
        LetterIcon.Letter(null).Should().Be("?");
        LetterIcon.ColorIndex("GitHub").Should().Be(LetterIcon.ColorIndex(" github "));
        Enumerable.Range(0, 200).Select(i => LetterIcon.ColorIndex("name" + i)).Should().OnlyContain(c => c >= 0 && c < LetterIcon.ColorCount);
    }

    [Fact]
    public void WhiteLetters_AreReadableOnEveryTile()
    {
        static double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        EntryIcon.Palette.Should().HaveCount(LetterIcon.ColorCount);
        foreach (var hex in EntryIcon.Palette)
        {
            var bytes = Convert.FromHexString(hex[1..]);
            var luminance = (0.2126 * Channel(bytes[0])) + (0.7152 * Channel(bytes[1])) + (0.0722 * Channel(bytes[2]));
            (1.05 / (luminance + 0.05)).Should().BeGreaterThanOrEqualTo(4.5, hex);
        }
    }

    private static OtpAccount Account(string issuer, string group = "") => new() { Issuer = issuer, Group = group, Secret = "JBSWY3DPEHPK3PXP" };

    [Fact]
    public void TapToReveal_CoversCodes_ShowsOne_AndCoversItAgain()
    {
        var now = DateTimeOffset.Parse("2026-11-02T10:00:00Z");
        var page = new DashboardViewModel(() => now, startTimer: false) { HideCodes = true };
        page.Load([Account("GitHub")]);
        var item = page.Accounts[0];

        item.DisplayCode.Should().Be("••• •••");
        item.Code.Should().HaveLength(6, "Copy code still works");

        page.ToggleRevealCommand.Execute(item);
        item.DisplayCode.Should().NotContain("•");

        now += OtpItemViewModel.RevealTime + TimeSpan.FromSeconds(1);
        page.Refresh();
        item.DisplayCode.Should().Be("••• •••");
    }

    [Fact]
    public void WithTapToRevealOff_CodesAreShown()
    {
        var page = new DashboardViewModel(startTimer: false);
        page.Load([Account("GitHub")]);

        page.Accounts[0].IsHidden.Should().BeFalse();
        page.Accounts[0].CanHide.Should().BeFalse();
    }

    [Fact]
    public void Accounts_CanBeMovedInYourOwnOrder_AndChangedOrderIsSaved()
    {
        var page = new DashboardViewModel(startTimer: false);
        page.Load([Account("A"), Account("B"), Account("C")]);
        var saves = 0;
        page.AccountsChanged += (_, _) => saves++;

        page.MoveDownCommand.Execute(page.Accounts[0]);
        page.MoveUpCommand.Execute(page.Accounts[2]);

        page.Accounts.Select(a => a.Issuer).Should().Equal("B", "C", "A");
        saves.Should().Be(2);

        page.MoveUpCommand.Execute(page.Accounts[0]);
        page.Accounts.Select(a => a.Issuer).Should().Equal("B", "C", "A");

        page.SearchText = "x";
        page.CanReorder.Should().BeFalse("moving in a filtered list would be confusing");
    }

    [Fact]
    public void Groups_FilterTheList_AndComeFromTheAccounts()
    {
        var page = new DashboardViewModel(startTimer: false);
        page.Load([Account("A", "Work"), Account("B", "Home"), Account("C")]);

        page.HasGroups.Should().BeTrue();
        page.GroupChoices.Skip(1).Should().Equal("Home", "Work");

        page.SelectedGroup = "Work";
        page.Shown.Select(a => a.Issuer).Should().Equal("A");

        page.SelectedGroup = page.GroupChoices[0];
        page.Shown.Should().HaveCount(3);
    }

    [Fact]
    public void Renaming_KeepsTheSecret_AndSetsTheGroup()
    {
        var page = new DashboardViewModel(startTimer: false);
        page.Load([Account("A")]);
        var id = page.Accounts[0].Id;

        page.BeginEditCommand.Execute(page.Accounts[0]);
        page.EditGroup = "Work";
        page.SaveEditCommand.Execute(null);

        var saved = page.Accounts.Single().Account;
        saved.Id.Should().Be(id);
        saved.Secret.Should().Be("JBSWY3DPEHPK3PXP");
        saved.Group.Should().Be("Work");
        saved.ModifiedUtc.Should().NotBeNull();
    }

    // Strong, different passwords, so only the checks this test is about find anything. Built from parts so that the secret
    // scanner does not take a test value for a real key.
    private static string Strong(int n) => string.Concat("a-Long-Unique-", "Passw0rd-", n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Health_FindsCopiesAddressesAndDates()
    {
        var now = DateTimeOffset.Parse("2026-11-02T10:00:00Z");
        var report = PasswordHealth.Analyze(
        [
            new PasswordEntry { Title = "One", UserName = "me", Password = Strong(1), Url = "https://site.example/", ModifiedUtc = now },
            new PasswordEntry { Title = "Two", UserName = "ME", Password = Strong(2), Url = "http://site.example", ModifiedUtc = now },
            new PasswordEntry { Title = "Late", Password = Strong(3), ExpiresUtc = now.AddDays(-2), ModifiedUtc = now },
            new PasswordEntry { Title = "Soon", Password = Strong(4), ExpiresUtc = now.AddDays(10), ModifiedUtc = now },
            new PasswordEntry { Title = "Far", Password = Strong(5), ExpiresUtc = now.AddDays(90), ModifiedUtc = now },
            new PasswordEntry { Title = "Binned", Password = "x", Url = "http://bin.example", ExpiresUtc = now.AddDays(-2), DeletedUtc = now },
        ], now);

        report.Duplicates.Select(d => d.Entry.Title).Should().BeEquivalentTo("One", "Two");
        report.Insecure.Select(d => d.Entry.Title).Should().Equal("Two");
        report.Expiring.Select(d => d.Entry.Title).Should().Equal("Late", "Soon");
        report.Expiring[0].Reason.Should().Contain("Expired 2 days ago");
    }

    [Fact]
    public void TheHealthPage_OffersASimpleFix()
    {
        var page = new PasswordsViewModel();
        page.Load([new PasswordEntry { Title = "Plain", Password = "p", Url = "http://site.example/login" }, new PasswordEntry { Title = "Copy", Password = "p" }]);
        var entries = page.Snapshot();

        page.UseHttpsById(entries[0].Id).Should().BeTrue();
        page.MoveToBinById(entries[1].Id);

        var after = page.Snapshot();
        after[0].Url.Should().Be("https://site.example/login");
        after[0].History.Should().ContainSingle(v => v.Url == "http://site.example/login", "the old address can be found again");
        after[1].IsDeleted.Should().BeTrue("a copy goes to the recycle bin, where it can be restored");
    }

    [Fact]
    public void Notes_Cards_AndIdentities_NeedNoPassword_AndKeepTheirFields()
    {
        var page = new PasswordsViewModel();
        page.Title = "Visa";
        page.FormKind = PasswordsViewModel.KindOptions.First(o => o.Kind == EntryKind.Card);

        page.FormFields.Select(f => f.Name).Should().Contain("Card number");
        page.FormFields.Single(f => f.Name == "Card number").IsSecret.Should().BeTrue();

        page.FormFields.Single(f => f.Name == "Card number").Value = "4111";
        page.FormExpires = new DateTime(2027, 1, 31);
        page.SaveCommand.Execute(null);

        var saved = page.Snapshot().Single();
        saved.Kind.Should().Be(EntryKind.Card);
        saved.Password.Should().BeEmpty();
        saved.Fields.Should().Contain(f => f.Name == "Card number" && f.Value == "4111");
        saved.Fields.Should().NotContain(f => f.Value.Length == 0 && f.Name.Length == 0);
        saved.ExpiresUtc!.Value.Year.Should().Be(2027);

        page.EditCommand.Execute(saved);
        page.FormKind.Kind.Should().Be(EntryKind.Card);
        page.FormFields.Should().HaveCount(saved.Fields.Count);
        page.FormExpires!.Value.Date.Should().Be(new DateTime(2027, 1, 31));
    }

    [Fact]
    public void ALogin_StillNeedsAPassword()
    {
        var page = new PasswordsViewModel { Title = "No password" };

        page.SaveCommand.Execute(null);

        page.Snapshot().Should().BeEmpty();
        page.Status.Should().Be("Password is required.");
    }

    [Fact]
    public void Editing_KeepsTheIcon_AndAChangedFieldGoesIntoTheHistory()
    {
        var page = new PasswordsViewModel();
        page.Load([new PasswordEntry
        {
            Title = "Bank", Password = "p", IconPng = "abc",
            Fields = [new CustomField { Name = "PIN", Value = "1234", IsSecret = true }],
        }]);

        page.EditCommand.Execute(page.Snapshot()[0]);
        page.FormFields[0].Value = "9999";
        page.SaveCommand.Execute(null);

        var saved = page.Snapshot()[0];
        saved.IconPng.Should().Be("abc");
        saved.History.Should().ContainSingle().Which.Fields[0].Value.Should().Be("1234");
    }

    [Fact]
    public void Searching_LeavesTheFoldersAlone_AndSameResultsRedoNothing()
    {
        var page = new PasswordsViewModel();
        page.Load([new PasswordEntry { Title = "Gmail", Password = "p", Folder = "Mail" }, new PasswordEntry { Title = "Bank", Password = "p", Folder = "Mail/Old" }]);
        var folders = page.FolderNodes.ToList();
        var changes = 0;
        page.Rows.CollectionChanged += (_, _) => changes++;

        page.SearchText = "gm";
        page.SearchText = "gma";
        page.SearchText = "gmail";

        page.FolderNodes.Should().Equal(folders);
        page.Rows.Should().ContainSingle();
        changes.Should().Be(1, "only the first letter changed what is shown");
    }

    [Fact]
    public void FolderCounts_IncludeSubfolders()
    {
        var page = new PasswordsViewModel();
        page.Load([new PasswordEntry { Title = "A", Password = "p", Folder = "Mail" }, new PasswordEntry { Title = "B", Password = "p", Folder = "mail/Old" }, new PasswordEntry { Title = "C", Password = "p" }]);

        page.FolderNodes.Single(n => n.Path == "Mail").Display.Should().Contain("2");
        page.FolderNodes[0].Display.Should().Contain("3");
    }

    [Fact]
    public void ATickIsNotKept_WhenTheNextSearchShowsTheSameEntries()
    {
        var page = new PasswordsViewModel();
        page.Load([new PasswordEntry { Title = "Gmail", Password = "p" }]);
        page.Rows[0].IsSelected = true;

        page.SearchText = "g";

        page.HasSelection.Should().BeFalse();
        page.Rows[0].IsSelected.Should().BeFalse("what looks ticked must be what would be deleted");
    }

    [Fact]
    public void LoadingManyAccounts_TellsTheScreenOnce()
    {
        var page = new DashboardViewModel(startTimer: false);
        var changes = 0;
        page.Shown.CollectionChanged += (_, _) => changes++;

        page.Load(Enumerable.Range(0, 500).Select(i => Account("A" + i)));
        page.AddRange([Account("One more"), Account("And another")]);

        page.Shown.Should().HaveCount(502);
        changes.Should().Be(2);
    }

    [Fact]
    public void TheTagFilter_ShowsOnlyThatTag()
    {
        var page = new PasswordsViewModel();
        page.Load([new PasswordEntry { Title = "A", Password = "p", Tags = ["work"] }, new PasswordEntry { Title = "B", Password = "p" }]);

        page.HasTags.Should().BeTrue();
        page.SelectedTag = "work";

        page.Entries.Should().ContainSingle().Which.Title.Should().Be("A");
    }

    [Fact]
    public void DeletingForGood_IsRemembered_SoSyncDoesNotBringItBack()
    {
        var page = new PasswordsViewModel();
        var entry = new PasswordEntry { Title = "A", Password = "p", DeletedUtc = DateTimeOffset.UtcNow };
        page.Load([entry]);

        page.DeleteCommand.Execute(entry);

        page.TombstoneSnapshot().Should().ContainSingle(t => t.Id == entry.Id);
    }

    [Fact]
    public void HidingOnFocusLoss_HidesRevealedPasswords_AndCanBeTurnedOff()
    {
        HeadlessUi.Run(() =>
        {
            AutoHideReveal.Install();
            var box = new Avalonia.Controls.TextBox { RevealPassword = true };

            AutoHideReveal.HideOnFocusLoss = false;
            AutoHideReveal.HideAll();
            box.RevealPassword.Should().BeTrue();

            AutoHideReveal.HideOnFocusLoss = true;
            AutoHideReveal.HideAll();
            box.RevealPassword.Should().BeFalse();
        });
    }

    // ---------------------------------------------------------------- emergency sheet

    [Fact]
    public void TheEmergencySheet_HoldsNoSecrets_NoScriptsAndNoLinks()
    {
        var html = EmergencySheet.BuildHtml(new EmergencySheetData(@"C:\v\vault.sauth", "KEYID", "D:\\b<script>", "https://dav.example/", DateTimeOffset.UtcNow));

        html.Should().Contain("KEYID").And.Contain("vault.sauth");
        html.Should().NotContain("<script", "text from the settings file is escaped").And.NotContain("href=").And.NotContain("src=");
        html.Should().Contain("Master password").And.Contain("class=\"box\"");
    }
}

/// <summary>Reads the exports of other apps. Every one of these files is someone else's and may be hostile.</summary>
public sealed class AppImportTests
{
    private static KeePassImportResult Read(string json) => AppExportImporter.ParseJson(json);

    [Fact]
    public void Bitwarden_Logins_Notes_Cards_AndFolders()
    {
        var result = Read("""
            {"encrypted":false,"folders":[{"id":"f1","name":"Work"}],"items":[
              {"type":1,"name":"Mail","folderId":"f1","notes":"n","fields":[{"name":"Q","value":"A","type":1}],
               "login":{"username":"me","password":"pw","totp":"JBSWY3DPEHPK3PXP","uris":[{"uri":"https://mail.example"}]}},
              {"type":2,"name":"Note","notes":"text"},
              {"type":3,"name":"Visa","card":{"cardholderName":"Me","number":"4111","expMonth":"1","expYear":"2030","code":"123"}}
            ]}
            """);

        result.Passwords.Should().HaveCount(3);
        var mail = result.Passwords[0];
        mail.Folder.Should().Be("Work");
        mail.Url.Should().Be("https://mail.example");
        mail.Fields.Should().ContainSingle(f => f.Name == "Q" && f.IsSecret);
        result.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("Mail");
        result.Passwords[1].Kind.Should().Be(EntryKind.Note);
        result.Passwords[2].Kind.Should().Be(EntryKind.Card);
        result.Passwords[2].Fields.Should().Contain(f => f.Name == "Card number" && f.Value == "4111" && f.IsSecret);
    }

    [Fact]
    public void AnEncryptedExport_IsRefusedWithAClearMessage()
    {
        var bitwarden = () => Read("""{"encrypted":true,"items":[]}""");
        bitwarden.Should().Throw<KeePassImportException>().WithMessage("*encrypted*");

        var aegis = () => Read("""{"version":1,"header":{"slots":[{}]},"db":"AAAA"}""");
        aegis.Should().Throw<KeePassImportException>().WithMessage("*encrypted*");

        var twoFas = () => Read("""{"services":[],"servicesEncrypted":"AAAA"}""");
        twoFas.Should().Throw<KeePassImportException>().WithMessage("*encrypted*");
    }

    [Fact]
    public void OnePassword_ItemsInVaults()
    {
        var result = Read("""
            {"accounts":[{"vaults":[{"attrs":{"name":"Private"},"items":[
              {"state":"active","categoryUuid":"001","updatedAt":1700000000,"overview":{"title":"Site","url":"https://site.example","tags":["a"]},
               "details":{"loginFields":[{"designation":"username","value":"me"},{"designation":"password","value":"pw"}],"notesPlain":"n",
                 "sections":[{"fields":[{"title":"one-time","value":{"totp":"otpauth://totp/x?secret=JBSWY3DPEHPK3PXP"}},{"title":"PIN","value":{"concealed":"1234"}}]}]}},
              {"state":"archived","overview":{"title":"Old"},"details":{}}
            ]}]}]}
            """);

        var entry = result.Passwords.Should().ContainSingle().Subject;
        entry.UserName.Should().Be("me");
        entry.Password.Should().Be("pw");
        entry.Folder.Should().Be("Private");
        entry.Fields.Should().ContainSingle(f => f.Name == "PIN" && f.IsSecret);
        entry.ModifiedUtc.ToUnixTimeSeconds().Should().Be(1700000000);
        result.Accounts.Should().ContainSingle();
        result.SkippedRecycleBin.Should().Be(1);
    }

    [Fact]
    public void ProtonPass_ItemsInVaults()
    {
        var result = Read("""
            {"encrypted":false,"vaults":{"s1":{"name":"Personal","items":[
              {"state":1,"modifyTime":1700000000,"data":{"type":"login","metadata":{"name":"Site","note":"n"},"extraFields":[{"fieldName":"X","type":"hidden","data":{"content":"secret"}}],
                "content":{"itemEmail":"me@x","password":"pw","urls":["https://site.example"],"totpUri":"otpauth://totp/x?secret=JBSWY3DPEHPK3PXP"}}},
              {"state":2,"data":{"type":"note","metadata":{"name":"Trashed"},"content":{}}},
              {"state":1,"data":{"type":"note","metadata":{"name":"Memo","note":"text"},"content":{}}}
            ]}}}
            """);

        result.Passwords.Should().HaveCount(2);
        result.Passwords[0].UserName.Should().Be("me@x");
        result.Passwords[0].Fields.Should().ContainSingle(f => f.IsSecret);
        result.Passwords[1].Kind.Should().Be(EntryKind.Note);
        result.Accounts.Should().ContainSingle();
        result.SkippedRecycleBin.Should().Be(1);
    }

    [Fact]
    public void Aegis_AccountsWithGroups()
    {
        var result = Read("""
            {"version":1,"header":{"slots":null,"params":null},"db":{"version":3,"groups":[{"uuid":"g1","name":"Work"}],"entries":[
              {"type":"totp","uuid":"u","name":"me","issuer":"GitHub","groups":["g1"],"info":{"secret":"JBSWY3DPEHPK3PXP","algo":"SHA256","digits":8,"period":60}},
              {"type":"hotp","name":"h","issuer":"H","info":{"secret":"JBSWY3DPEHPK3PXP","algo":"SHA1","digits":6,"counter":5}},
              {"type":"steam","name":"s","issuer":"Steam","info":{"secret":"JBSWY3DPEHPK3PXP"}}
            ]}}
            """);

        result.Accounts.Should().HaveCount(2);
        var totp = result.Accounts[0];
        totp.Group.Should().Be("Work");
        totp.Algorithm.Should().Be(OtpAlgorithm.Sha256);
        totp.Digits.Should().Be(8);
        totp.Period.Should().Be(60);
        result.Accounts[1].Type.Should().Be(OtpType.Hotp);
        result.Accounts[1].Counter.Should().Be(5);
        result.Warnings.Should().ContainSingle(w => w.Contains("Steam", StringComparison.Ordinal) || w.Contains("'s'", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoFas_Accounts()
    {
        var result = Read("""
            {"schemaVersion":4,"groups":[{"id":"g","name":"Home"}],"services":[
              {"name":"Site","secret":"JBSWY3DPEHPK3PXP","groupId":"g","otp":{"account":"me","issuer":"Site","digits":6,"period":30,"algorithm":"SHA1","tokenType":"TOTP"}}
            ]}
            """);

        var account = result.Accounts.Should().ContainSingle().Subject;
        account.Issuer.Should().Be("Site");
        account.Label.Should().Be("me");
        account.Group.Should().Be("Home");
    }

    [Fact]
    public void ACsvFromAnotherManager_IsUnderstood()
    {
        var bitwarden = KeePassImporter.ParseCsv("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\nWork,,login,Mail,hello,,,https://m.example,me,pw,JBSWY3DPEHPK3PXP\n,,note,Memo,text,,,,,,\n");

        bitwarden.Passwords.Should().HaveCount(2);
        bitwarden.Passwords[0].UserName.Should().Be("me");
        bitwarden.Passwords[0].Folder.Should().Be("Work");
        bitwarden.Passwords[1].Kind.Should().Be(EntryKind.Note);
        bitwarden.Accounts.Should().ContainSingle();

        var onePassword = KeePassImporter.ParseCsv("Title,Url,Username,Password,OTPAuth,Notes\nSite,https://s.example,me,pw,otpauth://totp/x?secret=JBSWY3DPEHPK3PXP,n\n");
        onePassword.Passwords.Should().ContainSingle();
        onePassword.Accounts.Should().ContainSingle();
    }

    [Fact]
    public void AListOfLinks_IsRead()
    {
        var result = AppExportImporter.ParseOtpLinks("otpauth://totp/A?secret=JBSWY3DPEHPK3PXP\nnot a link\n");

        result.Accounts.Should().ContainSingle();
        result.SkippedInvalid.Should().Be(1);
    }

    [Fact]
    public void AGoogleExportLink_IsRead()
    {
        // One account: secret "Hello!", name "GitHub:me", issuer "GitHub", SHA1, 6 digits, TOTP.
        byte[] secret = "Hello!"u8.ToArray();
        byte[] Bytes(int field, byte[] value) => [(byte)((field << 3) | 2), (byte)value.Length, .. value];
        byte[] Number(int field, byte value) => [(byte)(field << 3), value];
        byte[] account = [.. Bytes(1, secret), .. Bytes(2, "GitHub:me"u8.ToArray()), .. Bytes(3, "GitHub"u8.ToArray()), .. Number(4, 1), .. Number(5, 1), .. Number(6, 2)];
        byte[] payload = [.. Bytes(1, account), .. Number(2, 1)];
        var link = "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));

        GoogleMigration.TryParse(link, out var accounts).Should().BeTrue();

        var parsed = accounts.Single();
        parsed.Issuer.Should().Be("GitHub");
        parsed.Label.Should().Be("me");
        Base32.Decode(parsed.Secret).Should().Equal(secret);
    }

    [Theory]
    [InlineData("otpauth-migration://offline?data=")]
    [InlineData("otpauth-migration://offline?data=!!!!")]
    [InlineData("otpauth-migration://offline?data=CgM")]
    [InlineData("https://example.com")]
    public void ADamagedGoogleLink_IsRefused(string link) => GoogleMigration.TryParse(link, out _).Should().BeFalse();

    [Fact]
    public void AZipBomb_IsStoppedAtTheLimit()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("export.data", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i <= KeePassImporter.MaxFileBytes / zeros.Length; i++)
            {
                entry.Write(zeros);
            }
        }

        var read = () => AppExportImporter.ReadZip(output.ToArray());

        read.Should().Throw<KeePassImportException>().WithMessage("*too large*");
    }

    [Fact]
    public void A1PasswordZip_IsRead()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ms-1pux-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "export.1pux");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("export.data").Open());
                writer.Write("""{"accounts":[{"vaults":[{"attrs":{"name":"V"},"items":[{"overview":{"title":"T"},"details":{"password":"pw"}}]}]}]}""");
            }

            KeePassImporter.ReadFile(path).Passwords.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void JsonNobodyKnows_IsRefused()
    {
        var read = () => Read("""{"hello":"world"}""");
        read.Should().Throw<KeePassImportException>();
    }
}

/// <summary>Reads the KeePass file back with the format's own rules, as KeePass would.</summary>
public sealed class KdbxExportTests
{
    private static readonly Argon2Parameters FastKdf = new() { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };

    private static (XDocument Xml, List<byte[]> Binaries) Open(byte[] file, string password)
    {
        var reader = new BinaryReader(new MemoryStream(file));
        reader.ReadUInt32().Should().Be(0x9AA2D903);
        reader.ReadUInt32().Should().Be(0xB54BFB67);
        reader.ReadUInt32().Should().Be(0x00040000);

        byte[] seed = [], iv = [], kdf = [];
        while (true)
        {
            var id = reader.ReadByte();
            var data = reader.ReadBytes(reader.ReadInt32());
            switch (id)
            {
                case 0:
                    goto headerDone;
                case 4:
                    seed = data;
                    break;
                case 7:
                    iv = data;
                    break;
                case 11:
                    kdf = data;
                    break;
            }
        }

    headerDone:
        var headerLength = (int)reader.BaseStream.Position;
        var header = file[..headerLength];
        reader.ReadBytes(32).Should().Equal(SHA256.HashData(header), "the header hash");

        var kdfReader = new BinaryReader(new MemoryStream(kdf));
        kdfReader.ReadUInt16().Should().Be(0x0100);
        var values = new Dictionary<string, byte[]>();
        while (true)
        {
            var type = kdfReader.ReadByte();
            if (type == 0)
            {
                break;
            }

            var name = Encoding.ASCII.GetString(kdfReader.ReadBytes(kdfReader.ReadInt32()));
            values[name] = kdfReader.ReadBytes(kdfReader.ReadInt32());
        }

        values["$UUID"].Should().Equal(Convert.FromHexString("9E298B1956DB4773B23DFC3EC6F0A1E6"), "Argon2id");
        var composite = SHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        using var argon = new Konscious.Security.Cryptography.Argon2id(composite)
        {
            Salt = values["S"],
            MemorySize = (int)(BinaryPrimitives.ReadUInt64LittleEndian(values["M"]) / 1024),
            Iterations = (int)BinaryPrimitives.ReadUInt64LittleEndian(values["I"]),
            DegreeOfParallelism = (int)BinaryPrimitives.ReadUInt32LittleEndian(values["P"]),
        };
        var transformed = argon.GetBytes(32);
        var hmacBase = SHA512.HashData([.. seed, .. transformed, 1]);
        byte[] BlockKey(ulong index)
        {
            var number = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(number, index);
            return SHA512.HashData([.. number, .. hmacBase]);
        }

        reader.ReadBytes(32).Should().Equal(HMACSHA256.HashData(BlockKey(ulong.MaxValue), header), "the header check proves the password");

        using var payload = new MemoryStream();
        for (ulong index = 0; ; index++)
        {
            var mac = reader.ReadBytes(32);
            var size = reader.ReadInt32();
            var block = reader.ReadBytes(size);
            var signed = new byte[12 + size];
            BinaryPrimitives.WriteUInt64LittleEndian(signed, index);
            BinaryPrimitives.WriteInt32LittleEndian(signed.AsSpan(8), size);
            block.CopyTo(signed, 12);
            mac.Should().Equal(HMACSHA256.HashData(BlockKey(index), signed), $"block {index}");
            if (size == 0)
            {
                break;
            }

            payload.Write(block);
        }

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData([.. seed, .. transformed]);
        var plain = aes.DecryptCbc(payload.ToArray(), iv);
        using var gzip = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
        using var inner = new MemoryStream();
        gzip.CopyTo(inner);

        var innerReader = new BinaryReader(new MemoryStream(inner.ToArray()));
        var binaries = new List<byte[]>();
        while (true)
        {
            var id = innerReader.ReadByte();
            var data = innerReader.ReadBytes(innerReader.ReadInt32());
            if (id == 0)
            {
                break;
            }

            if (id == 3)
            {
                binaries.Add(data[1..]);
            }
        }

        var xml = innerReader.ReadBytes((int)(innerReader.BaseStream.Length - innerReader.BaseStream.Position));
        return (XDocument.Parse(Encoding.UTF8.GetString(xml)), binaries);
    }

    private static string Field(XElement entry, string key)
        => entry.Elements("String").Single(s => s.Element("Key")!.Value == key).Element("Value")!.Value;

    [Fact]
    public void TheFile_FollowsTheFormat_AndHoldsEverythingInUse()
    {
        var attachment = new PasswordAttachment { Name = "a.txt", Size = 3 };
        var contents = new VaultContents
        {
            Accounts = [new OtpAccount { Issuer = "GitHub", Label = "me", Secret = "JBSWY3DPEHPK3PXP" }],
            Passwords =
            [
                new PasswordEntry
                {
                    Title = "Mail", UserName = "me", Password = "pässword", Url = "https://m.example", Notes = "hello & <world>", Folder = "Work/Mail",
                    Tags = ["a", "b"], Attachments = [attachment], Fields = [new CustomField { Name = "PIN", Value = "12", IsSecret = true }],
                },
                new PasswordEntry { Title = "Visa", Password = string.Empty, Kind = EntryKind.Card, Fields = [new CustomField { Name = "Number", Value = "4111" }, new CustomField { Name = "Number", Value = "5222" }] },
                new PasswordEntry { Title = "Binned", Password = "x", DeletedUtc = DateTimeOffset.UtcNow },
            ],
            AttachmentData = new Dictionary<Guid, byte[]> { [attachment.Id] = [1, 2, 3] },
        };

        var (xml, binaries) = Open(KdbxWriter.Write(contents, "a-long-export-password", FastKdf), "a-long-export-password");

        var entries = xml.Descendants("Entry").ToList();
        entries.Should().HaveCount(3, "the recycle bin stays out");
        var mail = entries.Single(e => Field(e, "Title") == "Mail");
        Field(mail, "Password").Should().Be("pässword");
        Field(mail, "Notes").Should().Be("hello & <world>");
        Field(mail, "PIN").Should().Be("12");
        mail.Element("Tags")!.Value.Should().Be("a;b");
        mail.Parent!.Element("Name")!.Value.Should().Be("Mail");
        mail.Parent!.Parent!.Element("Name")!.Value.Should().Be("Work");
        binaries.Single().Should().Equal(1, 2, 3);

        var card = entries.Single(e => Field(e, "Title") == "Visa");
        Field(card, "Number").Should().Be("4111");
        Field(card, "Number (2)").Should().Be("5222", "KeePass allows each name once");

        var code = entries.Single(e => Field(e, "Title") == "GitHub");
        Field(code, "otp").Should().StartWith("otpauth://totp/");
    }

    [Fact]
    public void AWrongPassword_DoesNotPassTheHeaderCheck()
    {
        var file = KdbxWriter.Write(new VaultContents(), "a-long-export-password", FastKdf);

        var open = () => Open(file, "the-wrong-password");

        open.Should().Throw<Exception>();
    }

    [Fact]
    public void ACharacterXmlCannotCarry_IsLeftOut_SoTheFileStaysReadable()
    {
        var contents = new VaultContents { Passwords = [new PasswordEntry { Title = "A\u0001B", Password = "p" }] };

        var (xml, _) = Open(KdbxWriter.Write(contents, "x", FastKdf), "x");

        Field(xml.Descendants("Entry").Single(), "Title").Should().Be("AB");
    }
}

/// <summary>The website icon: every rule in the plan, against a hostile site.</summary>
public sealed class WebsiteIconTests
{
    private static byte[] Png(int width = 16, int height = 16)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private sealed class Site(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Redirect(string to)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return response;
    }

    [Theory]
    [InlineData("https://www.example.com/login?user=me", "www.example.com")]
    [InlineData("example.com", "example.com")]
    [InlineData("http://example.com", "example.com")]
    [InlineData("https://user:pw@example.com/", "example.com")]
    [InlineData("https://bücher.example/", "xn--bcher-kva.example")]
    public void APublicName_IsUsed_AndNothingElseOfTheAddress(string url, string host)
        => WebsiteIconFetcher.PublicHostOf(url).Should().Be(host);

    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://intranet/")]
    [InlineData("https://server.local/")]
    [InlineData("https://printer.lan/")]
    [InlineData("https://app.internal/")]
    [InlineData("https://localhost/")]
    [InlineData("https://2130706433/")]
    [InlineData("https://0x7f.0.0.1/")]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///c:/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsRefused(string? url) => WebsiteIconFetcher.PublicHostOf(url).Should().BeNull();

    [Theory]
    [InlineData("93.184.216.34", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("2001:0:7f00:1::", false)]
    [InlineData("2001:db8::1", false)]
    public void OnlyPublicAddresses_AreUsed(string address, bool allowed)
        => WebsiteIconFetcher.IsPublicAddress(IPAddress.Parse(address)).Should().Be(allowed);

    [Theory]
    [InlineData("https://www.example.com/favicon.ico", true)]
    [InlineData("https://example.com/favicon.ico", true)]
    [InlineData("http://www.example.com/favicon.ico", false)]
    [InlineData("https://evil.example/favicon.ico", false)]
    [InlineData("https://example.com.evil.example/favicon.ico", false)]
    [InlineData("https://www.example.com/other.ico", false)]
    [InlineData("https://www.example.com/favicon.ico?x=1", false)]
    [InlineData("https://www.example.com:8443/favicon.ico", false)]
    [InlineData("https://user@www.example.com/favicon.ico", false)]
    public void ARedirect_IsOnlyFollowedToTheSameSite(string target, bool allowed)
        => WebsiteIconFetcher.IsAllowedRedirect("example.com", new Uri(target)).Should().Be(allowed);

    [Fact]
    public async Task OnlyTheIconIsAskedFor_WithNoPathQueryOrName()
    {
        var site = new Site(_ => Ok(Png()));

        var result = await new WebsiteIconFetcher(site).FetchAsync("https://me:pw@www.example.com/secret/path?token=abc");

        result.PngBase64.Should().NotBeNull();
        var request = site.Requests.Single();
        request.RequestUri!.ToString().Should().Be("https://www.example.com/favicon.ico");
        request.Headers.Should().BeEmpty("no cookies, no referrer, nothing about the user");
    }

    [Fact]
    public async Task TheStoredIcon_IsA32By32PngMadeHere_NotTheDownloadedFile()
    {
        var original = Png(64, 64);
        var result = await new WebsiteIconFetcher(new Site(_ => Ok(original))).FetchAsync("example.com");

        var stored = Convert.FromBase64String(result.PngBase64!);
        stored.Should().NotEqual(original);
        using var codec = SKCodec.Create(new SKMemoryStream(stored));
        codec.Info.Width.Should().Be(32);
        codec.Info.Height.Should().Be(32);
        codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Png);
    }

    [Fact]
    public async Task ARedirectToAnotherSite_IsNotFollowed()
    {
        var site = new Site(_ => Redirect("https://evil.example/favicon.ico"));

        var result = await new WebsiteIconFetcher(site).FetchAsync("example.com");

        result.PngBase64.Should().BeNull();
        site.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RedirectToHttp_AndChains_AreRefused()
    {
        var http = new Site(_ => Redirect("http://www.example.com/favicon.ico"));
        (await new WebsiteIconFetcher(http).FetchAsync("example.com")).PngBase64.Should().BeNull();

        var chain = new Site(r => r.RequestUri!.Host == "example.com" ? Redirect("https://www.example.com/favicon.ico") : Redirect("https://example.com/favicon.ico"));
        (await new WebsiteIconFetcher(chain).FetchAsync("example.com")).PngBase64.Should().BeNull();
        chain.Requests.Should().HaveCount(2, "only one redirect is ever followed");
    }

    [Fact]
    public async Task HugeFiles_AreStopped()
    {
        var site = new Site(_ => Ok(new byte[IconImageCheck.MaxBytes + 1]));

        (await new WebsiteIconFetcher(site).FetchAsync("example.com")).Problem.Should().Contain("too large");
    }

    [Fact]
    public async Task AFileThatNeverEnds_IsStoppedByTheLimit()
    {
        var site = new Site(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) });

        (await new WebsiteIconFetcher(site).FetchAsync("example.com")).Problem.Should().Contain("too large");
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => count;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><body>not found</body></html>")]
    [InlineData("GIF89a..........")]
    public async Task AnythingThatIsNotPngOrIco_IsRefused(string body)
    {
        var site = new Site(_ => Ok(Encoding.UTF8.GetBytes(body)));

        (await new WebsiteIconFetcher(site).FetchAsync("example.com")).PngBase64.Should().BeNull();
    }

    [Fact]
    public void APictureClaimingToBeEnormous_IsRefusedBeforeItIsDecoded()
    {
        // A valid header saying 100000 by 100000 pixels, the classic memory attack.
        var png = Png();
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 100_000);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 100_000);

        IconImageCheck.Normalize(png).Should().BeNull();
        IconImageCheck.Normalize(Png(IconImageCheck.MaxDimension + 1, 4)).Should().BeNull();
    }

    [Fact]
    public async Task AHostileServer_ThatFails_GivesAPlainAnswer()
    {
        var site = new Site(_ => throw new HttpRequestException("no route"));
        (await new WebsiteIconFetcher(site).FetchAsync("example.com")).Problem.Should().Contain("could not be reached");

        var slow = new SlowSite();
        var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        (await new WebsiteIconFetcher(slow).FetchAsync("example.com", cancelled.Token)).Problem.Should().Contain("too long");
    }

    private sealed class SlowSite : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    [Fact]
    public void TheRealHandler_FollowsTheRules()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MasterSafe.App", "Services", "WebsiteIconFetcher.cs"));

        source.Should().Contain("AllowAutoRedirect = false").And.Contain("UseCookies = false").And.Contain("UseProxy = false").And.Contain("ConnectCallback = ConnectToPublicAddressAsync");
        source.Should().NotContain("ServerCertificateCustomValidationCallback", "a certificate error is never overlooked");
    }

    [Fact]
    public async Task TheButton_DoesNothing_WhileTheSettingIsOff()
    {
        var asked = 0;
        var page = new PasswordsViewModel
        {
            Url = "example.com",
            IconFetcher = _ =>
            {
                asked++;
                return Task.FromResult(new IconResult("abc", null));
            },
        };

        await page.FetchIconCommand.ExecuteAsync(null);

        asked.Should().Be(0);
        page.HasIconPreview.Should().BeFalse();
        page.ShowIconButton.Should().BeFalse();
    }

    [Fact]
    public async Task AnIcon_IsOnlyKeptAfterTheUserAcceptsIt()
    {
        var page = new PasswordsViewModel
        {
            Title = "Site",
            Password = "p",
            Url = "example.com",
            IconsAllowed = () => true,
            IconFetcher = _ => Task.FromResult(new IconResult("abc", null)),
        };

        await page.FetchIconCommand.ExecuteAsync(null);
        page.HasIconPreview.Should().BeTrue();
        page.RejectIconCommand.Execute(null);
        page.SaveCommand.Execute(null);
        page.Snapshot().Single().IconPng.Should().BeNull();

        page.EditCommand.Execute(page.Snapshot()[0]);
        await page.FetchIconCommand.ExecuteAsync(null);
        page.AcceptIconCommand.Execute(null);
        page.SaveCommand.Execute(null);
        page.Snapshot().Single().IconPng.Should().Be("abc");
    }
}

/// <summary>Random damage against everything new that reads outside data.</summary>
[Trait("Category", "Fuzz")]
public sealed class Release16FuzzTests
{
    private static readonly int Iterations = int.TryParse(Environment.GetEnvironmentVariable("MASTERSAFE_FUZZ_ITERATIONS"), out var n) ? n : 1500;

    private static readonly int Seed = int.TryParse(Environment.GetEnvironmentVariable("MASTERSAFE_FUZZ_SEED"), out var s) ? s : Environment.TickCount;

    private static bool IsCleanRefusal(Exception ex) => ex is FormatException or InvalidDataException or KeePassImportException
        or JsonException or CryptographicException or ArgumentException or InvalidPasswordException;

    private static void Run(string name, byte[] sample, Action<byte[]> read)
    {
        var random = new Random(Seed);
        for (var i = 0; i < Iterations; i++)
        {
            var data = (byte[])sample.Clone();
            for (var changes = random.Next(1, 8); changes > 0 && data.Length > 0; changes--)
            {
                data[random.Next(data.Length)] = (byte)random.Next(256);
            }

            if (random.Next(4) == 0)
            {
                data = data[..random.Next(data.Length + 1)];
            }

            try
            {
                read(data);
            }
            catch (Exception ex) when (IsCleanRefusal(ex))
            {
                // Refused cleanly.
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{name}: seed {Seed}, round {i}: {ex.GetType().Name} on input {Convert.ToBase64String(data)}", ex);
            }
        }
    }

    [Theory]
    [InlineData("""{"encrypted":false,"folders":[{"id":"f","name":"W"}],"items":[{"type":1,"name":"M","folderId":"f","fields":[{"name":"Q","value":"A","type":1}],"login":{"username":"m","password":"p","totp":"x","uris":[{"uri":"u"}]}},{"type":3,"name":"V","card":{"number":"4"}},{"type":4,"name":"I","identity":{"firstName":"a"}}]}""")]
    [InlineData("""{"accounts":[{"vaults":[{"attrs":{"name":"P"},"items":[{"state":"active","updatedAt":1700000000,"overview":{"title":"S","tags":["a"]},"details":{"loginFields":[{"designation":"password","value":"p"}],"sections":[{"fields":[{"title":"T","value":{"totp":"x"}}]}]}}]}]}]}""")]
    [InlineData("""{"encrypted":false,"vaults":{"s":{"name":"P","items":[{"state":1,"data":{"type":"login","metadata":{"name":"S"},"extraFields":[],"content":{"password":"p","urls":["u"]}}}]}}}""")]
    [InlineData("""{"version":1,"header":{"slots":null},"db":{"groups":[{"uuid":"g","name":"W"}],"entries":[{"type":"totp","name":"m","issuer":"G","groups":["g"],"info":{"secret":"JBSWY3DPEHPK3PXP","algo":"SHA1","digits":6,"period":30}}]}}""")]
    [InlineData("""{"groups":[{"id":"g","name":"H"}],"services":[{"name":"S","secret":"JBSWY3DPEHPK3PXP","groupId":"g","otp":{"account":"m","digits":6,"period":30,"algorithm":"SHA1"}}]}""")]
    public void AppExports_AreRefusedWhenDamaged(string sample)
        => Run("AppExportImporter", Encoding.UTF8.GetBytes(sample), d => AppExportImporter.ParseJson(Encoding.UTF8.GetString(d)));

    [Fact]
    public void GoogleLinks_AreRefusedWhenDamaged()
        => Run("GoogleMigration", "otpauth-migration://offline?data=CjEKBkhlbGxvIRIJR2l0SHViOm1lGgZHaXRIdWIgASgBMAIQARgBIAA%3D"u8.ToArray(), d => GoogleMigration.TryParse(Encoding.UTF8.GetString(d), out _));

    [Fact]
    public void ZipExports_AreRefusedWhenDamaged()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("export.data").Open());
            writer.Write("""{"accounts":[]}""");
        }

        Run("ReadZip", output.ToArray(), d => AppExportImporter.ReadZip(d));
    }

    [Fact]
    public void TheImageCheck_OnlyEverSaysYesOrNo()
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        var png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        // A one-image ICO that wraps the PNG.
        byte[] ico = [0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, .. BitConverter.GetBytes(png.Length), 22, 0, 0, 0, .. png];

        Run("IconImageCheck PNG", png, d => IconImageCheck.Normalize(d));
        Run("IconImageCheck ICO", ico, d => IconImageCheck.Normalize(d));
        IconImageCheck.Normalize(ico).Should().NotBeNull("the sample must really be a valid ICO, or this test proves nothing");
    }

    [Fact]
    public void SyncFiles_AreRefusedWhenDamaged()
    {
        var key = SyncCode.NewKey();
        var encryption = new AesGcmEncryptionService();
        var sample = SyncPackage.Seal(new VaultContents { Passwords = [new PasswordEntry { Title = "A", Password = "p" }] }, key, "n", encryption);

        Run("SyncPackage", sample, d =>
        {
            var opened = SyncPackage.Open(d, key, "n", encryption);
            opened.Passwords.Should().ContainSingle("a file that opens must be the original");
            d.Should().Equal(sample);
        });
    }

    [Fact]
    public void Manifests_AreRefusedWhenDamaged()
    {
        var sample = JsonSerializer.SerializeToUtf8Bytes(new ReleaseManifest(ReleaseManifest.ProductName, "1.6.0", [new ReleaseManifestFile("a.exe", "00")]));

        Run("ReleaseManifest", sample, d => ReleaseManifest.TryParse(d));
    }

    [Fact]
    public void NotesBlocks_AreHandledWhenDamaged()
    {
        var sample = EntryExtras.Attach(new PasswordEntry { Title = "x", Password = string.Empty, Kind = EntryKind.Card, Fields = [new CustomField { Name = "n", Value = "v" }] }).Notes;

        Run("EntryExtras", Encoding.UTF8.GetBytes(sample), d => EntryExtras.Detach(new PasswordEntry { Title = "x", Password = "p", Notes = Encoding.UTF8.GetString(d) }));
    }
}
