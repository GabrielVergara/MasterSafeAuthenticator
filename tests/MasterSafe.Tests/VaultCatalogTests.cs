// File:    VaultCatalogTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App;
using MasterSafe.App.Services;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public sealed class VaultCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly SettingsStore _settings;
    private readonly SwitchableVaultRepository _repository;

    public VaultCatalogTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(_paths.DataFolder);
        _settings = new SettingsStore(_paths.SettingsPath);
        _repository = new SwitchableVaultRepository(_paths.DefaultVaultPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private VaultCatalog NewCatalog() => new(_paths, _repository, _settings);

    private void WriteVault(string path, string content = "{}")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Paths_AreUnderTheDataFolder()
    {
        _paths.DefaultVaultPath.Should().EndWith("vault.sauth");
        _paths.IsInsideDataFolder(_paths.DefaultVaultPath).Should().BeTrue();
        _paths.IsInsideDataFolder(_paths.ArchiveFolder).Should().BeTrue();
        _paths.IsInsideDataFolder(Path.Combine(_root, "elsewhere", "x.sauth")).Should().BeFalse();
        _paths.IsInsideDataFolder(_paths.DataFolder + "-evil" + Path.DirectorySeparatorChar + "x.sauth").Should().BeFalse();
        _paths.IsInsideDataFolder(Path.Combine(_paths.DataFolder, "..", "outside.sauth")).Should().BeFalse();
    }

    [Fact]
    public void StartsOnTheDefaultVault()
    {
        NewCatalog().ActivePath.Should().Be(_paths.DefaultVaultPath);
    }

    [Fact]
    public void ArchiveDefaultVault_MovesFileAndBackup_AndNothingIsLost()
    {
        WriteVault(_paths.DefaultVaultPath, "old vault");
        WriteVault(_paths.DefaultVaultPath + ".bak", "old backup");
        var catalog = NewCatalog();

        var archived = catalog.ArchiveDefaultVault();

        archived.Should().NotBeNull();
        File.Exists(_paths.DefaultVaultPath).Should().BeFalse();
        File.ReadAllText(archived!).Should().Be("old vault");
        File.ReadAllText(archived + ".bak").Should().Be("old backup");
        _paths.IsInsideDataFolder(archived!).Should().BeTrue();
        catalog.List().Should().ContainSingle().Which.Kind.Should().Be(VaultKind.Archived);
    }

    [Fact]
    public void ArchiveDefaultVault_WithNoVault_DoesNothing()
    {
        NewCatalog().ArchiveDefaultVault().Should().BeNull();
    }

    [Fact]
    public void StartNewVault_NeverOverwritesAnExistingVault()
    {
        WriteVault(_paths.DefaultVaultPath, "precious");
        var catalog = NewCatalog();

        var archived = catalog.StartNewVault();

        File.ReadAllText(archived!).Should().Be("precious");
        File.Exists(_paths.DefaultVaultPath).Should().BeFalse();
        catalog.ActivePath.Should().Be(_paths.DefaultVaultPath);

        // Two archives made in the same second must not collide.
        WriteVault(_paths.DefaultVaultPath, "second");
        var second = catalog.StartNewVault();
        second.Should().NotBe(archived);
        File.ReadAllText(second!).Should().Be("second");
        File.ReadAllText(archived!).Should().Be("precious");
    }

    [Fact]
    public void OpenArchivedVault_MakesItActive_AndIsRemembered()
    {
        WriteVault(_paths.DefaultVaultPath, "old");
        var catalog = NewCatalog();
        var archived = catalog.StartNewVault()!;

        catalog.SetActive(archived);

        catalog.ActivePath.Should().Be(archived);
        _repository.Path.Should().Be(archived);
        _settings.Load().ActiveVaultPath.Should().Be(archived);

        // A new catalog (next start of the app) opens the same vault.
        var restarted = new VaultCatalog(_paths, new SwitchableVaultRepository(_paths.DefaultVaultPath), _settings);
        restarted.ActivePath.Should().Be(archived);
    }

    [Fact]
    public void ExternalVault_IsListedKeptInPlace_AndCanBeForgotten()
    {
        var external = Path.Combine(_root, "usb", "friend.sauth");
        WriteVault(external, "external");
        var catalog = NewCatalog();

        catalog.SetActive(external);

        var entry = catalog.List().Should().ContainSingle(e => e.Kind == VaultKind.External).Subject;
        entry.IsActive.Should().BeTrue();
        entry.Path.Should().Be(Path.GetFullPath(external));

        catalog.Forget(external);

        File.Exists(external).Should().BeTrue();
        catalog.List().Should().NotContain(e => e.Kind == VaultKind.External);
        catalog.ActivePath.Should().Be(_paths.DefaultVaultPath);
    }

    [Fact]
    public void SetActive_MissingFile_Throws()
    {
        var act = () => NewCatalog().SetActive(Path.Combine(_root, "nope.sauth"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Delete_RemovesArchivedVaultAndBackup()
    {
        WriteVault(_paths.DefaultVaultPath, "old");
        WriteVault(_paths.DefaultVaultPath + ".bak", "bak");
        var catalog = NewCatalog();
        var archived = catalog.StartNewVault()!;

        catalog.Delete(archived);

        File.Exists(archived).Should().BeFalse();
        File.Exists(archived + ".bak").Should().BeFalse();
        catalog.List().Should().BeEmpty();
    }

    [Fact]
    public void Delete_RefusesFilesOutsideTheAppFolder()
    {
        var external = Path.Combine(_root, "documents", "important.sauth");
        WriteVault(external, "mine");
        var catalog = NewCatalog();

        var act = () => catalog.Delete(external);

        act.Should().Throw<InvalidOperationException>();
        File.Exists(external).Should().BeTrue();

        var traversal = () => catalog.Delete(Path.Combine(_paths.DataFolder, "..", "documents", "important.sauth"));
        traversal.Should().Throw<InvalidOperationException>();
        File.Exists(external).Should().BeTrue();
    }

    [Fact]
    public void Delete_RefusesNonVaultFiles()
    {
        WriteVault(_paths.SettingsPath, "{}");
        var act = () => NewCatalog().Delete(_paths.SettingsPath);
        act.Should().Throw<InvalidOperationException>();
        File.Exists(_paths.SettingsPath).Should().BeTrue();
    }

    [Fact]
    public void Delete_OfTheActiveVault_FallsBackToTheDefaultPath()
    {
        WriteVault(_paths.DefaultVaultPath, "old");
        var catalog = NewCatalog();
        var archived = catalog.StartNewVault()!;
        catalog.SetActive(archived);

        catalog.Delete(archived);

        catalog.ActivePath.Should().Be(_paths.DefaultVaultPath);
    }

    [Fact]
    public void Settings_RoundTrip_AndSurviveDamage()
    {
        var settings = new AppSettings
        {
            Theme = AppTheme.Dark,
            MinimizeToTray = false,
            AutoLockMinutes = 15,
            RecoveryPublicKey = "AAAA",
        };
        _settings.Save(settings);

        var loaded = _settings.Load();
        loaded.Theme.Should().Be(AppTheme.Dark);
        loaded.MinimizeToTray.Should().BeFalse();
        loaded.AutoLockMinutes.Should().Be(15);
        loaded.RecoveryPublicKey.Should().Be("AAAA");

        File.Exists(_paths.SettingsPath + ".tmp").Should().BeFalse();

        File.WriteAllText(_paths.SettingsPath, "{ this is not json");
        _settings.Load().Theme.Should().Be(AppTheme.System);
    }

    [Fact]
    public void Settings_AreSanitisedOnLoad()
    {
        File.WriteAllText(_paths.SettingsPath, """
            { "AutoLockMinutes": 100000, "ActiveVaultPath": "relative.sauth", "KnownVaultPaths": ["", "relative", "%S"] }
            """.Replace("%S", Path.Combine(_root, "x.sauth").Replace("\\", "\\\\")));

        var loaded = _settings.Load();

        loaded.AutoLockMinutes.Should().Be(120);
        loaded.ActiveVaultPath.Should().BeNull();
        loaded.KnownVaultPaths.Should().ContainSingle();
    }

    [Fact]
    public async Task SwitchableRepository_FollowsThePath()
    {
        var a = Path.Combine(_root, "a.sauth");
        var b = Path.Combine(_root, "b.sauth");
        var repo = new SwitchableVaultRepository(a);
        File.WriteAllText(b, "{}");

        (await repo.ExistsAsync()).Should().BeFalse();
        repo.SetPath(b);
        (await repo.ExistsAsync()).Should().BeTrue();
        repo.Path.Should().Be(b);
    }
}
