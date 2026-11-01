// File:    VaultSnapshotTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public sealed class VaultSnapshotTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-snap-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public VaultSnapshotTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private string VaultPath => Path.Combine(_folder, "vault.sauth");

    private string Snapshots => Path.Combine(_folder, "snapshots");

    private static VaultFile Vault(string id) => new() { VaultId = id, Version = 3 };

    private FileVaultRepository Repo() => new(VaultPath, Snapshots, () => _now);

    [Fact]
    public async Task TheFirstSaveEverMakesNoSnapshot_BecauseThereIsNothingToKeep()
    {
        await Repo().SaveAsync(Vault("one"));

        Directory.Exists(Snapshots).Should().BeFalse();
    }

    [Fact]
    public async Task TheFirstSaveOfADayKeepsTheVaultAsItWas_LaterSavesThatDayDoNot()
    {
        var repo = Repo();
        await repo.SaveAsync(Vault("one"));

        _now = _now.AddDays(1);
        await repo.SaveAsync(Vault("two"));
        await repo.SaveAsync(Vault("three"));

        var snapshot = Directory.GetFiles(Snapshots).Should().ContainSingle().Subject;
        Path.GetFileName(snapshot).Should().Be("vault-20261102.sauth");
        File.ReadAllText(snapshot).Should().Contain("one", "it holds the vault as it was before the day's first save");
        (await repo.LoadAsync())!.VaultId.Should().Be("three");
    }

    [Fact]
    public async Task OnlyTheLastSevenDaysAreKept()
    {
        var repo = Repo();
        await repo.SaveAsync(Vault("start"));
        for (var day = 1; day <= 12; day++)
        {
            _now = _now.AddDays(1);
            await repo.SaveAsync(Vault("day" + day));
        }

        var names = Directory.GetFiles(Snapshots).Select(Path.GetFileName).OrderBy(n => n).ToList();

        names.Should().HaveCount(FileVaultRepository.SnapshotsKept);
        names[^1].Should().Be("vault-20261113.sauth");
        names[0].Should().Be("vault-20261107.sauth");
    }

    [Fact]
    public async Task OtherFilesInTheSnapshotFolder_AreNeverDeleted()
    {
        Directory.CreateDirectory(Snapshots);
        File.WriteAllText(Path.Combine(Snapshots, "notes.txt"), "mine");
        File.WriteAllText(Path.Combine(Snapshots, "other-20200101.sauth"), "another vault");
        var repo = Repo();
        await repo.SaveAsync(Vault("a"));
        for (var day = 1; day <= 10; day++)
        {
            _now = _now.AddDays(1);
            await repo.SaveAsync(Vault("b" + day));
        }

        File.Exists(Path.Combine(Snapshots, "notes.txt")).Should().BeTrue();
        File.Exists(Path.Combine(Snapshots, "other-20200101.sauth")).Should().BeTrue();
    }

    [Fact]
    public async Task AVaultWithoutASnapshotFolder_NeverCreatesOne()
    {
        var repo = new FileVaultRepository(VaultPath, null, () => _now);
        await repo.SaveAsync(Vault("one"));
        _now = _now.AddDays(1);
        await repo.SaveAsync(Vault("two"));

        Directory.Exists(Snapshots).Should().BeFalse();
    }

    [Fact]
    public async Task SnapshotsOnlyHappenForVaultsInTheAppsOwnFolder()
    {
        var other = Path.Combine(_folder, "usb");
        Directory.CreateDirectory(other);
        var switchable = new SwitchableVaultRepository(VaultPath);
        await switchable.SaveAsync(Vault("home-1"));
        await switchable.SaveAsync(Vault("home-2"));

        switchable.SetPath(Path.Combine(other, "elsewhere.sauth"));
        await switchable.SaveAsync(Vault("usb-1"));
        await switchable.SaveAsync(Vault("usb-2"));

        Directory.GetDirectories(other).Should().BeEmpty("no snapshot folder next to a vault on a USB stick");
    }

    [Fact]
    public async Task ASnapshotProblem_NeverStopsTheSave()
    {
        var repo = Repo();
        await repo.SaveAsync(Vault("one"));
        await File.WriteAllTextAsync(Snapshots, "a file where the folder should be");
        _now = _now.AddDays(1);

        var act = () => repo.SaveAsync(Vault("two"));

        await act.Should().NotThrowAsync();
        (await repo.LoadAsync())!.VaultId.Should().Be("two");
    }
}
