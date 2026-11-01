// File:    SecurityReviewTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.App.Views;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>Regression tests for the problems found in the 1.5.0 security review.</summary>
public sealed class SecurityReviewTests : IDisposable
{
    private static readonly Argon2Parameters FastKdf = new() { MemorySizeKib = 1024, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-review-" + Guid.NewGuid().ToString("N"));

    public SecurityReviewTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Loc.Use("en");
        Directory.Delete(_folder, true);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;

    private (VaultService Vault, string Path, string Snapshots) NewVault()
    {
        var path = Path.Combine(_folder, "vault.sauth");
        var snapshots = Path.Combine(_folder, "snapshots");
        var now = DateTime.UtcNow;
        var repository = new FileVaultRepository(path, snapshots, () => now);
        return (new VaultService(repository, new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf), path, snapshots);
    }

    private static readonly VaultContents Sample = new()
    {
        Accounts = [new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }],
    };

    [Fact]
    public async Task ChangingTheMasterPassword_RemovesTheOldCopiesTheOldPasswordStillOpens()
    {
        var (vault, path, snapshots) = NewVault();
        await vault.SaveContentsAsync("old-password", Sample);
        await vault.SaveContentsAsync("old-password", Sample);
        File.Exists(path + ".bak").Should().BeTrue();
        Directory.GetFiles(snapshots).Should().NotBeEmpty();

        await vault.RotateMasterPasswordAsync("old-password", "new-password");

        File.Exists(path + ".bak").Should().BeFalse("the old password opens that file, and with it the data key of the current vault");
        Directory.GetFiles(snapshots).Should().BeEmpty();
        (await vault.OpenContentsAsync("new-password")).Accounts.Should().ContainSingle();
    }

    [Fact]
    public async Task SnapshotsOfAnotherVault_AreLeftAlone()
    {
        var (vault, _, snapshots) = NewVault();
        await vault.SaveContentsAsync("old-password", Sample);
        Directory.CreateDirectory(snapshots);
        var other = Path.Combine(snapshots, "other-20260101.sauth");
        File.WriteAllText(other, "x");

        await vault.RotateMasterPasswordAsync("old-password", "new-password");

        File.Exists(other).Should().BeTrue();
    }

    private sealed class FixedServer(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MultiStatus) { Content = new StringContent(body, Encoding.UTF8) });
    }

    private static WebDavBackupProvider Server(string body)
        => new(new HttpClient(new FixedServer(body)), new Uri("https://example.com/backups/"));

    [Fact]
    public async Task AWebDavServer_CannotDefineEntities()
    {
        const string Hostile = """
            <?xml version="1.0"?><!DOCTYPE d [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <D:multistatus xmlns:D="DAV:"><D:response><D:href>/backups/&b;.sauth</D:href></D:response></D:multistatus>
            """;

        (await Server(Hostile).ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AWebDavServer_CannotSendAnEndlessListing()
    {
        var act = () => Server(new string('x', WebDavBackupProvider.MaxListingChars + 10)).ListAsync();

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void ASecretBox_NeverGivesItsTextToAccessibilityTools()
    {
        HeadlessUi.Run(() =>
        {
            foreach (var reveal in new[] { false, true })
            {
                var box = new SecretTextBox { Text = "hunter2", RevealPassword = reveal };

                var provider = ControlAutomationPeer.CreatePeerForElement(box).GetProvider<IValueProvider>();

                provider!.Value.Should().Be("●●●●●●●");
                provider.SetValue("typed by voice");
                box.Text.Should().Be("typed by voice");
            }
        });
    }

    [Fact]
    public void EveryPasswordFieldOnEveryScreen_IsASecretBox()
    {
        var views = Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "src", "MasterSafe.App", "Views");
        var plain = Directory.GetFiles(views, "*.axaml").Where(f => File.ReadAllText(f).Contains("<TextBox Classes=\"revealPasswordButton\"", StringComparison.Ordinal)
            || File.ReadAllText(f).Contains("PasswordChar=", StringComparison.Ordinal));
        plain.Should().BeEmpty();
    }

    [Fact]
    public void TheUsersOwnText_IsNeverTranslated()
    {
        HeadlessUi.Run(() =>
        {
            Loc.Install();
            Loc.Use("fi");

            var entryTitle = new TextBlock { Tag = "data" };
            entryTitle.Text = "Password";
            entryTitle.Text.Should().Be("Password");

            var label = new TextBlock { Text = "Password" };
            label.Text.Should().Be("Salasana");
        });
    }

    [Fact]
    public void AVeryLongText_DoesNotSlowTheTranslationDown()
    {
        Loc.Use("fi");
        var text = new string('a', 200_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Loc.T(text).Should().Be(text);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Exit_CanBeChosenOnTheSignInScreenAndInTheSidebar()
    {
        var vm = new MainWindowViewModel();
        var exits = 0;
        vm.ExitRequested = () => exits++;

        vm.ExitCommand.Execute(null);
        vm.Login.ExitCommand.Execute(null);

        exits.Should().Be(2);
    }

    [Fact]
    public async Task Locking_ClearsThePasswordTypedToStrengthenTheVault()
    {
        var (vault, _, _) = NewVault();
        await vault.SaveContentsAsync("old-password", Sample);
        var vm = new MainWindowViewModel(vault);
        await vm.UnlockVaultAsync("old-password");
        vm.Settings.StrengthenPassword = "typed";

        vm.LockNow();

        vm.Settings.StrengthenPassword.Should().BeEmpty();
    }
}
