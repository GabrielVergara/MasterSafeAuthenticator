// File:    DropImportTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Crypto;
using MasterSafe.QR;
using Xunit;

namespace MasterSafe.Tests;

public sealed class DropImportTests : IDisposable
{
    private const string Password = "a long test master password";

    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-drop-" + Guid.NewGuid().ToString("N"));
    private readonly VaultService _vault;
    private readonly QrCodeGenerator _generator = new();

    public DropImportTests()
    {
        Directory.CreateDirectory(_folder);
        _vault = new VaultService(
            new FileVaultRepository(Path.Combine(_folder, "vault.sauth")),
            new AesGcmEncryptionService(),
            new MasterPasswordService(),
            FastKdf);
    }

    public void Dispose()
    {
        // Importing starts a background save of the vault, so the folder may still be in use for a moment.
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
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private async Task<MainWindowViewModel> UnlockedShellAsync()
    {
        await _vault.CreateAsync(Password);
        var shell = new MainWindowViewModel(_vault);
        await shell.UnlockVaultAsync(Password);
        shell.IsUnlocked.Should().BeTrue();
        return shell;
    }

    private string Qr(string name, string text, string extension = ".png")
    {
        var path = Path.Combine(_folder, name + extension);
        File.WriteAllBytes(path, _generator.GeneratePng(text, 300));
        return path;
    }

    private static string Uri(string issuer, string label, string secret)
        => $"otpauth://totp/{issuer}:{label}?secret={secret}&issuer={issuer}";

    [Fact]
    public async Task DroppedQrImage_IsImported_ShownOnTheDashboard_AndSaved()
    {
        var shell = await UnlockedShellAsync();
        var path = Qr("github", Uri("GitHub", "alice@example.com", "JBSWY3DPEHPK3PXP"));

        var imported = shell.ImportDroppedFiles([path]);

        imported.Should().Be(1);
        shell.Dashboard.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("GitHub");
        shell.ActiveSection.Should().Be("Dashboard");
        shell.Dashboard.StatusMessage.Should().Contain("Imported GitHub");

        await shell.PersistAsync();
        (await _vault.OpenContentsAsync(Password)).Accounts.Should().ContainSingle().Which.Secret.Should().Be("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public async Task QrWithoutAnIssuer_UsesTheLabelInTheMessage()
    {
        var shell = await UnlockedShellAsync();
        var path = Qr("test", "otpauth://totp/totp@authenticationtest.com?secret=JBSWY3DPEHPK3PXP");

        shell.ImportDroppedFiles([path]).Should().Be(1);

        shell.Dashboard.StatusMessage.Should().Contain("totp@authenticationtest.com").And.NotContain("Imported  ");
    }

    [Fact]
    public void NothingIsImported_WhileTheVaultIsLocked()
    {
        var shell = new MainWindowViewModel(_vault);
        var path = Qr("github", Uri("GitHub", "alice@example.com", "JBSWY3DPEHPK3PXP"));

        shell.ImportDroppedFiles([path]).Should().Be(0);

        shell.Dashboard.Accounts.Should().BeEmpty();
        shell.IsUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task DroppingTheSameQrTwice_DoesNotCreateADuplicate()
    {
        var shell = await UnlockedShellAsync();
        var path = Qr("github", Uri("GitHub", "alice@example.com", "JBSWY3DPEHPK3PXP"));
        shell.ImportDroppedFiles([path]).Should().Be(1);

        var again = shell.ImportDroppedFiles([path]);

        again.Should().Be(0);
        shell.Dashboard.Accounts.Should().HaveCount(1);
        shell.Import.Status.Should().Contain("already in your vault");
        shell.ActiveSection.Should().Be("Import", "the reason should be visible");
    }

    [Fact]
    public async Task SeveralImages_AreImportedTogether_AndTheSummaryCountsThem()
    {
        var shell = await UnlockedShellAsync();
        var a = Qr("a", Uri("GitHub", "a@example.com", "JBSWY3DPEHPK3PXP"));
        var b = Qr("b", Uri("Mail", "b@example.com", "GEZDGNBVGY3TQOJQ"));
        var junk = Qr("junk", "https://example.com");

        var imported = shell.ImportDroppedFiles([a, junk, b]);

        imported.Should().Be(2);
        shell.Dashboard.Accounts.Should().HaveCount(2);
        shell.Import.Status.Should().Be("Imported 2 of 3 images.");
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".gif")]
    [InlineData(".sauth")]
    public async Task OtherFileTypes_AreRefusedWithAClearMessage(string extension)
    {
        var shell = await UnlockedShellAsync();
        var path = Path.Combine(_folder, "file" + extension);
        File.WriteAllText(path, "not an image");

        shell.ImportDroppedFiles([path]).Should().Be(0);

        shell.Import.Status.Should().Contain("not a PNG, JPG or BMP");
        shell.Dashboard.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task AQrCodeThatIsNotAnAuthenticator_IsRefused()
    {
        var shell = await UnlockedShellAsync();
        var path = Qr("web", "https://example.com/login");

        shell.ImportDroppedFiles([path]).Should().Be(0);

        shell.Import.Status.Should().Contain("No valid authenticator QR code");
    }

    [Fact]
    public async Task ABrokenImageFile_IsRefusedWithoutCrashing()
    {
        var shell = await UnlockedShellAsync();
        var path = Path.Combine(_folder, "broken.png");
        File.WriteAllText(path, "this is not really a png");

        shell.ImportDroppedFiles([path]).Should().Be(0);

        shell.Import.Status.Should().NotBeNullOrEmpty();
        shell.Dashboard.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task ADropOfNothing_ReportsIt()
    {
        var shell = await UnlockedShellAsync();

        shell.ImportDroppedFiles([]).Should().Be(0);

        shell.Import.Status.Should().Be("No file was dropped.");
    }

    [Fact]
    public async Task AHugeDrop_ReadsOnlyTheFirstFewImages()
    {
        var shell = await UnlockedShellAsync();
        var paths = Enumerable.Range(0, ImportViewModel.MaxImagesPerDrop + 5)
            .Select(i => Qr($"qr{i}", Uri("Svc" + i, $"u{i}@example.com", "JBSWY3DPEHPK3PXP")))
            .ToList();

        var imported = shell.ImportDroppedFiles(paths);

        imported.Should().Be(ImportViewModel.MaxImagesPerDrop);
        shell.Import.Status.Should().Contain($"Only the first {ImportViewModel.MaxImagesPerDrop} were read");
    }

    [Fact]
    public async Task ChooseAnImage_UsesThePickerAndImports()
    {
        var shell = await UnlockedShellAsync();
        var path = Qr("picked", Uri("GitHub", "alice@example.com", "JBSWY3DPEHPK3PXP"));
        shell.Import.Dialogs = new PickerThatReturns(path);

        await shell.Import.BrowseImageCommand.ExecuteAsync(null);

        shell.Dashboard.Accounts.Should().ContainSingle();
    }

    [Fact]
    public async Task CancellingThePicker_ImportsNothing()
    {
        var shell = await UnlockedShellAsync();
        shell.Import.Dialogs = new PickerThatReturns(null);

        await shell.Import.BrowseImageCommand.ExecuteAsync(null);

        shell.Dashboard.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSameLinkTypedTwice_IsNotAddedTwice()
    {
        var shell = await UnlockedShellAsync();
        shell.Import.ManualUri = Uri("GitHub", "alice@example.com", "JBSWY3DPEHPK3PXP");
        shell.Import.ImportUriCommand.Execute(null);
        shell.Import.ManualUri = Uri("GitHub", "alice@example.com", "jbswy3dp ehpk3pxp");

        shell.Import.ImportUriCommand.Execute(null);

        shell.Dashboard.Accounts.Should().HaveCount(1);
        shell.Import.Status.Should().Contain("already in your vault");
    }

    private sealed class PickerThatReturns(string? path) : IFileDialogs
    {
        public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult(path);

        public Task<string?> SaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public void ShowInFolder(string path)
        {
        }
    }
}
