// File:    DistributionTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using MasterSafe.App;
using MasterSafe.App.ViewModels;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>The winget and Flathub packages, and an app that a store keeps up to date.</summary>
public sealed partial class DistributionTests
{
    private const string AppId = "io.github.GabrielVergara.MasterSafeAuthenticator";

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MasterSafe.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository was not found.");
    }

    private static string Version() => VersionTag().Match(File.ReadAllText(Path.Combine(Root(), "Directory.Build.props"))).Groups[1].Value;

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(AppId, "Flathub")]
    public void InsideAFlatpak_FlathubProvidesTheUpdates(string? flatpakId, string? expected)
        => AppInfo.StoreFor(flatpakId).Should().Be(expected);

    [Fact]
    public async Task AStoreCopy_NeverLooksForUpdatesItself()
    {
        var updates = new UpdatesViewModel(checker: null, updatedBy: "Flathub");

        updates.UpdatesItself.Should().BeFalse();
        updates.StoreText.Should().Contain("Flathub").And.Contain("flatpak update");
        updates.CheckCommand.CanExecute(null).Should().BeFalse("Flathub forbids an app that updates itself");
        (await updates.CheckAsync()).Should().BeFalse();

        new UpdatesViewModel().UpdatesItself.Should().BeTrue();
    }

    [Fact]
    public void TheFlathubData_DescribesThisVersion()
    {
        var folder = Path.Combine(Root(), "packaging", "flatpak");
        var metainfo = XDocument.Load(Path.Combine(folder, AppId + ".metainfo.xml")).Root!;

        metainfo.Element("id")!.Value.Should().Be(AppId);
        metainfo.Element("launchable")!.Value.Should().Be(AppId + ".desktop");
        metainfo.Element("releases")!.Elements("release").First().Attribute("version")!.Value
            .Should().Be(Version(), "every release needs its notes in the metainfo, newest first");

        var desktop = File.ReadAllText(Path.Combine(folder, AppId + ".desktop"));
        desktop.Should().Contain("Icon=" + AppId).And.Contain("Exec=mastersafe");

        File.ReadAllBytes(Path.Combine(folder, "logo.svg"))
            .Should().Equal(File.ReadAllBytes(Path.Combine(Root(), "src", "MasterSafe.App", "Assets", "logo.svg")));

        File.ReadAllText(Path.Combine(folder, AppId + ".yml")).Should().Contain("id: " + AppId)
            .And.NotContain("--filesystem", "files go through the desktop's file dialogs")
            .And.NotContain("--device=all");
    }

    [Fact]
    public void TheWingetManifests_AreForAPublishedVersion()
    {
        var folders = Directory.GetDirectories(Path.Combine(Root(), "packaging", "winget"));
        folders.Should().NotBeEmpty();
        foreach (var folder in folders)
        {
            var version = Path.GetFileName(folder);
            foreach (var file in Directory.GetFiles(folder, "*.yaml"))
            {
                var text = File.ReadAllText(file);
                text.Should().Contain("PackageIdentifier: GabrielVergara.MasterSafeAuthenticator").And.Contain("PackageVersion: " + version);
            }

            File.ReadAllText(Path.Combine(folder, "GabrielVergara.MasterSafeAuthenticator.installer.yaml"))
                .Should().Contain($"/download/v{version}/MasterSafe-Authenticator-{version}-win-x64-setup.exe")
                .And.Contain("{5B0F6F0E-3A57-4C69-9D54-7C1C7E5A2D41}_is1", "winget must recognise copies installed without it");
        }
    }

    [GeneratedRegex(@"<Version>([^<]+)</Version>")]
    private static partial Regex VersionTag();
}

/// <summary>Release signatures can be checked on systems without the operating system's ML-DSA (OpenSSL 3.0, Linux Mint 22).</summary>
public sealed class ManagedVerifyTests
{
    [Fact]
    public void TheManagedVerifier_AgreesWithTheSystemOne()
    {
        if (!MasterSafe.PostQuantum.ReleaseSigner.IsSupported)
        {
            return; // Signing needs the system's ML-DSA.
        }

        var (publicKey, seed) = MasterSafe.PostQuantum.ReleaseSigner.GenerateKeyPair();
        var data = System.Text.Encoding.UTF8.GetBytes("MasterSafe-Authenticator-1.7.1-linux-x64.tar.gz");
        var signature = MasterSafe.PostQuantum.ReleaseSigner.Sign(data, seed);

        MasterSafe.PostQuantum.ReleaseSigner.VerifyManaged(data, signature, publicKey).Should().BeTrue();

        var changed = (byte[])data.Clone();
        changed[0] ^= 1;
        MasterSafe.PostQuantum.ReleaseSigner.VerifyManaged(changed, signature, publicKey).Should().BeFalse();
        var badSignature = (byte[])signature.Clone();
        badSignature[100] ^= 1;
        MasterSafe.PostQuantum.ReleaseSigner.VerifyManaged(data, badSignature, publicKey).Should().BeFalse();
        MasterSafe.PostQuantum.ReleaseSigner.VerifyManaged(data, signature[..^1], publicKey).Should().BeFalse();
        MasterSafe.PostQuantum.ReleaseSigner.VerifyManaged(data, signature, new byte[publicKey.Length]).Should().BeFalse();
        MasterSafe.PostQuantum.ReleaseSigner.CanVerify.Should().BeTrue();
    }
}
