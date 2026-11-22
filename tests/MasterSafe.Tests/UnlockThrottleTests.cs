// File:    UnlockThrottleTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Backup;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public sealed class UnlockThrottleTests : IDisposable
{
    private const string Password = "a long test master password";

    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-throttle-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public UnlockThrottleTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(_folder); attempt++)
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private SettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private DateTimeOffset Clock() => _now;

    private UnlockThrottle Throttle(ISettingsStore? store = null) => new(Clock, store);

    private void Fail(UnlockThrottle t, int times)
    {
        for (var i = 0; i < times; i++)
        {
            t.RecordFailure();
        }
    }

    [Fact]
    public void TheFirstFiveWrongPasswordsCostNothing()
    {
        var t = Throttle();

        for (var i = 1; i <= 4; i++)
        {
            t.RecordFailure();
            t.IsBlocked.Should().BeFalse($"attempt {i} is still free");
            t.AttemptsLeft.Should().Be(5 - i);
        }
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(8, 300)]
    [InlineData(9, 900)]
    [InlineData(10, 900)]
    [InlineData(40, 900)]
    public void FromTheFifthMistake_TheWaitGrowsUpToFifteenMinutes(int failures, int seconds)
    {
        var t = Throttle();

        Fail(t, failures);

        t.IsBlocked.Should().BeTrue();
        t.Remaining.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void TheWaitCountsDownWithTheClock_AndEnds()
    {
        var t = Throttle();
        Fail(t, 5);

        _now += TimeSpan.FromSeconds(10);
        t.Remaining.Should().Be(TimeSpan.FromSeconds(20));

        _now += TimeSpan.FromSeconds(20);
        t.IsBlocked.Should().BeFalse();
        t.Remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ACorrectPassword_ResetsEverything_AndReportsTheMistakes()
    {
        var t = Throttle();
        Fail(t, 6);

        t.RecordSuccess().Should().Be(6);

        t.IsBlocked.Should().BeFalse();
        t.Failures.Should().Be(0);
        t.AttemptsLeft.Should().Be(5);
        t.RecordSuccess().Should().Be(0);
    }

    [Fact]
    public void TheCounterAndThePauseSurviveARestart()
    {
        var store = Store();
        Fail(Throttle(store), 5);

        var afterRestart = Throttle(store);

        afterRestart.Failures.Should().Be(5);
        afterRestart.IsBlocked.Should().BeTrue();
        afterRestart.Remaining.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AClockMovedBackOrAFileEdited_CannotMakeThePauseLongerThanTheLongestOne()
    {
        var store = Store();
        store.Save(new AppSettings { UnlockFailures = 5, UnlockBlockedUntilUtc = _now.AddYears(50).ToString("O") });

        var t = Throttle(store);

        t.Remaining.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void NonsenseInTheSettingsFile_IsHarmless()
    {
        var store = Store();
        store.Save(new AppSettings { UnlockFailures = -50, UnlockBlockedUntilUtc = "not a date" });

        var t = Throttle(store);

        t.Failures.Should().Be(0);
        t.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void SavingTheCounter_KeepsOtherSettings()
    {
        var store = Store();
        store.Save(new AppSettings { RecoveryPublicKey = "abc", AutoLockMinutes = 9 });

        Fail(Throttle(store), 3);

        var saved = store.Load();
        saved.RecoveryPublicKey.Should().Be("abc");
        saved.AutoLockMinutes.Should().Be(9);
        saved.UnlockFailures.Should().Be(3);
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(27, "27 seconds")]
    [InlineData(89, "89 seconds")]
    [InlineData(90, "2 minutes")]
    [InlineData(300, "5 minutes")]
    public void Describe_ReadsNaturally(int seconds, string expected)
        => UnlockThrottle.Describe(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    // ------------------------------------------------------------------ at the sign-in screen

    private async Task<(MainWindowViewModel Shell, VaultService Vault)> ShellAsync()
    {
        var vault = new VaultService(
            new FileVaultRepository(Path.Combine(_folder, "vault.sauth")),
            new AesGcmEncryptionService(),
            new MasterPasswordService(),
            FastKdf);
        if (!await vault.ExistsAsync())
        {
            await vault.CreateAsync(Password);
        }

        var shell = new MainWindowViewModel(vault, clock: Clock);
        return (shell, vault);
    }

    [Fact]
    public async Task WrongPasswords_ShowHowManyTriesAreLeft_ThenPauseTheScreen()
    {
        var (shell, _) = await ShellAsync();

        await shell.UnlockVaultAsync("wrong password 1");
        shell.Login.Error.Should().Contain("4 tries left");
        shell.Login.IsLockedOut.Should().BeFalse();

        for (var i = 0; i < 3; i++)
        {
            await shell.UnlockVaultAsync("wrong password x");
        }

        shell.Login.Error.Should().Contain("1 try left");

        await shell.UnlockVaultAsync("wrong password 5");

        shell.Login.IsLockedOut.Should().BeTrue();
        shell.Login.LockoutMessage.Should().Contain("Try again in 30 seconds");
        shell.Login.UnlockCommand.CanExecute(null).Should().BeFalse();
        shell.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task DuringThePause_EvenTheRightPasswordIsNotTried()
    {
        var (shell, _) = await ShellAsync();
        for (var i = 0; i < 5; i++)
        {
            await shell.UnlockVaultAsync("wrong password");
        }

        await shell.UnlockVaultAsync(Password);

        shell.IsUnlocked.Should().BeFalse("a paused sign-in does not even check the password");
    }

    [Fact]
    public async Task WhenThePauseEnds_TheRightPasswordWorks_AndTheUserIsToldAboutTheMistakes()
    {
        var (shell, _) = await ShellAsync();
        for (var i = 0; i < 5; i++)
        {
            await shell.UnlockVaultAsync("wrong password");
        }

        _now += TimeSpan.FromSeconds(31);
        shell.UpdateLockout();
        shell.Login.IsLockedOut.Should().BeFalse();
        shell.Login.LockoutMessage.Should().BeNull();

        await shell.UnlockVaultAsync(Password);

        shell.IsUnlocked.Should().BeTrue();
        shell.Dashboard.StatusMessage.Should().Contain("5 wrong passwords were entered since your last sign-in");
    }

    [Fact]
    public void ThePause_IsTimedByTheTickCount_SoMovingTheClockDoesNotEndIt()
    {
        long ticks = 0;
        var throttle = new UnlockThrottle(Clock, null, new ElapsedClock(() => ticks));
        Fail(throttle, 5);

        _now += TimeSpan.FromHours(3);
        throttle.IsBlocked.Should().BeTrue("the clock was moved forward, but no time passed");

        ticks += 31_000;
        throttle.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ACounterTurnedBackInTheSettings_IsNoticedAtTheNextSignIn()
    {
        var paths = new AppPaths(Path.Combine(_folder, "data"));
        Directory.CreateDirectory(paths.DataFolder);
        var settings = new SettingsStore(paths.SettingsPath);
        var repository = new SwitchableVaultRepository(paths.DefaultVaultPath);
        var hybrid = MasterSafe.PostQuantum.PostQuantumProviderFactory.CreateHybrid();
        var services = new AppServices(
            paths, new VaultCatalog(paths, repository, settings), settings, new NoDialogs(),
            new RecoveryKeyService(new AesGcmEncryptionService(), new MasterPasswordService(), hybrid, FastKdf),
            new RecipientBackupService(new AesGcmEncryptionService(), hybrid));
        var vault = new VaultService(repository, new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);
        await vault.CreateAsync(Password);

        // Three wrong passwords, then a correct one: the vault remembers 3 for this computer.
        var first = new MainWindowViewModel(vault, clock: Clock, services: services);
        for (var i = 0; i < 3; i++)
        {
            await first.UnlockVaultAsync("wrong password");
        }

        await first.UnlockVaultAsync(Password);
        first.Dashboard.StatusMessage.Should().Contain("3 wrong passwords");
        first.LockNow();

        // Someone guesses twice more and then puts both counters back to 0 in the settings file.
        var guesser = new MainWindowViewModel(vault, clock: Clock, services: services);
        await guesser.UnlockVaultAsync("guess 1");
        await guesser.UnlockVaultAsync("guess 2");
        var edited = settings.Load();
        edited.UnlockFailures = 0;
        edited.UnlockFailuresTotal = 0;
        settings.Save(edited);

        var owner = new MainWindowViewModel(vault, clock: Clock, services: services);
        await owner.UnlockVaultAsync(Password);

        owner.IsUnlocked.Should().BeTrue();
        owner.Dashboard.StatusMessage.Should().Contain("turned back outside MasterSafe");
        owner.SecurityLog.Snapshot().Should().Contain(e => e.Kind == SecurityEventsViewModel.SignInCounterChanged);
    }

    [Fact]
    public async Task ACounterOnlyResetInTheSettings_StillReportsTheGuesses()
    {
        var (shell, _) = await ShellAsync();
        await shell.UnlockVaultAsync(Password);
        shell.LockNow();

        for (var i = 0; i < 2; i++)
        {
            await shell.UnlockVaultAsync("wrong password");
        }

        await shell.UnlockVaultAsync(Password);
        shell.Dashboard.StatusMessage.Should().Contain("2 wrong passwords");
    }

    [Fact]
    public async Task ASuccessfulUnlockWithoutMistakes_SaysNothing()
    {
        var (shell, _) = await ShellAsync();

        await shell.UnlockVaultAsync(Password);

        shell.IsUnlocked.Should().BeTrue();
        shell.Dashboard.StatusMessage.Should().NotContain("wrong password");
    }

    [Fact]
    public async Task LockNow_LocksAnUnlockedVault_AndIgnoresALockedOne()
    {
        var (shell, _) = await ShellAsync();
        shell.LockNow();
        shell.IsUnlocked.Should().BeFalse();

        await shell.UnlockVaultAsync(Password);
        shell.IsUnlocked.Should().BeTrue();

        shell.LockNow();

        shell.IsUnlocked.Should().BeFalse();
        shell.Passwords.Snapshot().Should().BeEmpty();
    }
}

internal sealed class NoDialogs : IFileDialogs
{
    public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string? startFolder = null) => Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public void ShowInFolder(string path)
    {
    }
}
