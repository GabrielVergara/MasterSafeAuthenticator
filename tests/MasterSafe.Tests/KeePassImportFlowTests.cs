// File:    KeePassImportFlowTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public sealed class KeePassImportFlowTests : IDisposable
{
    private const string Password = "a long test master password";

    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-kpflow-" + Guid.NewGuid().ToString("N"));
    private readonly VaultService _vault;

    public KeePassImportFlowTests()
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
        return shell;
    }

    private string Export(string name = "export.csv")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(
            path,
            "Group,Title,Username,Password,URL,Notes,TOTP\n"
            + "Root/Email,Gmail,alice@example.com,pw1,https://mail.google.com,,otpauth://totp/Google:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Google\n"
            + "Root,Bank,jo,pw2,,,\n");
        return path;
    }

    [Fact]
    public async Task Preview_AddsNothing_UntilConfirmed()
    {
        var shell = await UnlockedShellAsync();

        shell.Import.KeePass.Load(Export()).Should().BeTrue();

        shell.Import.KeePass.HasPreview.Should().BeTrue();
        shell.Import.KeePass.Preview.Should().Contain("Passwords to add: 2").And.Contain("Authenticator accounts to add: 1");
        shell.Passwords.Snapshot().Should().BeEmpty();
        shell.Dashboard.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_AddsPasswordsAndAccounts_AndSavesThem()
    {
        var shell = await UnlockedShellAsync();
        shell.Import.KeePass.Load(Export());

        shell.Import.KeePass.ConfirmCommand.Execute(null);
        await shell.PersistAsync();

        shell.Passwords.Snapshot().Select(p => p.Title).Should().BeEquivalentTo("Gmail", "Bank");
        shell.Dashboard.Accounts.Should().ContainSingle().Which.Issuer.Should().Be("Google");
        shell.Import.KeePass.HasPreview.Should().BeFalse();
        shell.ActiveSection.Should().Be("Passwords");

        var saved = await _vault.OpenContentsAsync(Password);
        saved.Passwords.Should().HaveCount(2);
        saved.Accounts.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportingTheSameFileTwice_AddsNothingTheSecondTime()
    {
        var shell = await UnlockedShellAsync();
        shell.Import.KeePass.Load(Export());
        shell.Import.KeePass.ConfirmCommand.Execute(null);

        var again = shell.Import.KeePass.Load(Export());

        again.Should().BeFalse();
        shell.Import.KeePass.Status.Should().Contain("Nothing new");
        shell.Import.KeePass.Preview.Should().Contain("Already in your vault, skipped: 3");
        shell.Passwords.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task Cancel_ThrowsThePreviewAway()
    {
        var shell = await UnlockedShellAsync();
        shell.Import.KeePass.Load(Export());

        shell.Import.KeePass.CancelCommand.Execute(null);

        shell.Import.KeePass.HasPreview.Should().BeFalse();
        shell.Import.KeePass.Preview.Should().BeEmpty();
        shell.Passwords.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task LockingTheVault_DiscardsAPendingPreview()
    {
        var shell = await UnlockedShellAsync();
        shell.Import.KeePass.Load(Export());

        shell.LockCommand.Execute(null);

        shell.Import.KeePass.HasPreview.Should().BeFalse();
        shell.Import.KeePass.Preview.Should().BeEmpty();
    }

    [Fact]
    public async Task ADroppedExport_OpensThePreview_InsteadOfBeingRefusedAsAnImage()
    {
        var shell = await UnlockedShellAsync();

        shell.ImportDroppedFiles([Export()]).Should().Be(0);

        shell.Import.KeePass.HasPreview.Should().BeTrue();
        shell.ActiveSection.Should().Be("Import");
        shell.Passwords.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ABrokenFile_ShowsAMessage_AndNoPreview()
    {
        var shell = await UnlockedShellAsync();
        var path = Path.Combine(_folder, "bad.xml");
        File.WriteAllText(path, "<html/>");

        shell.Import.KeePass.Load(path).Should().BeFalse();

        shell.Import.KeePass.Status.Should().Contain("KeePass");
        shell.Import.KeePass.HasPreview.Should().BeFalse();
    }
}
