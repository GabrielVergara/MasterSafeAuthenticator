// File:    InstalledFilesTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Net;
using System.Text;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public sealed class InstalledFilesTests : IDisposable
{
    private const string Download = "https://github.com/GabrielVergara/MasterSafeAuthenticator/releases/download/v9.9.9/";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-installed-" + Guid.NewGuid().ToString("N"));

    public InstalledFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "runtimes"));
        File.WriteAllText(Path.Combine(_folder, "MasterSafe.App.dll"), "app");
        File.WriteAllText(Path.Combine(_folder, "runtimes", "native.so"), "native");
        File.WriteAllText(Path.Combine(_folder, "readme.txt"), "text");
        File.WriteAllBytes(Path.Combine(_folder, InstalledFiles.FileName), InstalledFiles.Build(_folder, "9.9.9", "win-x64"));
    }

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void AnUntouchedFolder_MatchesItsList_AndTheUninstallerIsIgnored()
    {
        File.WriteAllText(Path.Combine(_folder, "unins000.exe"), "added by the installer");

        var report = InstalledFiles.Check(_folder);

        report.Ok.Should().BeTrue();
        report.Version.Should().Be("9.9.9");
        report.Platform.Should().Be("win-x64");
    }

    [Fact]
    public void ChangedMissingAndPlantedFiles_AreFound()
    {
        File.WriteAllText(Path.Combine(_folder, "MasterSafe.App.dll"), "replaced");
        File.Delete(Path.Combine(_folder, "runtimes", "native.so"));
        File.WriteAllText(Path.Combine(_folder, "version.dll"), "planted next to the program");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not code, so not reported");

        var report = InstalledFiles.Check(_folder);

        report.Ok.Should().BeFalse();
        report.Changed.Should().Equal("MasterSafe.App.dll");
        report.Missing.Should().Equal("runtimes/native.so");
        report.Unexpected.Should().Equal("version.dll");
        ProgramFiles.Problems(report).Should().Be("MasterSafe.App.dll, runtimes/native.so, version.dll");
    }

    [Fact]
    public void AFolderWithoutAList_IsNotChecked()
    {
        File.Delete(Path.Combine(_folder, InstalledFiles.FileName));

        InstalledFiles.Check(_folder).Available.Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x.dll")]
    [InlineData("sub\\x.dll")]
    public void AListThatPointsOutsideTheFolder_IsRefused(string path)
    {
        var text = $"# MasterSafe Authenticator installed files 9.9.9 win-x64\n{new string('a', 64)}  {path}\n";

        InstalledFiles.Parse(Encoding.UTF8.GetBytes(text)).Should().BeNull();
    }

    [Fact]
    public async Task TheListIsConfirmed_OnlyWhenTheSignedReleaseHasTheSameOne()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var (publicKey, seed) = ReleaseSigner.GenerateKeyPair();
        var local = File.ReadAllBytes(Path.Combine(_folder, InstalledFiles.FileName));
        var name = InstalledFiles.AssetName("9.9.9", "win-x64");

        async Task<bool?> ConfirmWith(byte[] releaseList, byte[] signingSeed)
        {
            var release = Path.Combine(_folder, "release-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(release);
            File.WriteAllBytes(Path.Combine(release, name), releaseList);
            var manifest = ReleaseManifest.Build(release, "9.9.9", [name]);
            var signature = Convert.ToBase64String(ReleaseSigner.Sign(manifest, signingSeed));
            var assets = new[] { ReleaseManifest.FileName, ReleaseManifest.FileName + ".sig", name };
            var json = "{\"tag_name\":\"v9.9.9\",\"assets\":[" + string.Join(",", assets.Select(a => $"{{\"name\":\"{a}\",\"browser_download_url\":\"{Download}{a}\",\"size\":1}}")) + "]}";
            var handler = new Handler(url => url switch
            {
                _ when url == UpdateChecker.TagUrlPrefix + "9.9.9" => Encoding.UTF8.GetBytes(json),
                _ when url.EndsWith(".sig", StringComparison.Ordinal) => Encoding.ASCII.GetBytes(signature),
                _ when url.EndsWith(ReleaseManifest.FileName, StringComparison.Ordinal) => manifest,
                _ => releaseList,
            });
            return await new UpdateChecker(new HttpClient(handler), publicKey, "9.9.9").ConfirmInstalledFilesAsync(local);
        }

        (await ConfirmWith(local, seed)).Should().BeTrue();

        var other = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(local).Replace("readme.txt", "readme.TXT", StringComparison.Ordinal));
        (await ConfirmWith(other, seed)).Should().BeFalse("the folder's list was changed after the release");

        (await ConfirmWith(local, ReleaseSigner.GenerateKeyPair().PrivateSeed)).Should().BeFalse("the release was signed with another key");
    }

    private sealed class Handler(Func<string, byte[]> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(respond(request.RequestUri!.ToString())) });
    }
}
