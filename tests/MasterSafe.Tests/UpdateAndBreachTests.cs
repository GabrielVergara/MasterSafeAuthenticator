// File:    UpdateAndBreachTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public sealed class UpdateAndBreachTests : IDisposable
{
    private const string Base = "https://github.com/GabrielVergara/MasterSafeAuthenticator/releases/download/v9.9.9/";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-update-" + Guid.NewGuid().ToString("N"));

    public UpdateAndBreachTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    /// <summary>A web server the test controls.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    // ------------------------------------------------------------------ release information

    private static string ReleaseJson(string tag, params string[] assetNames)
    {
        var assets = string.Join(",", assetNames.Select(n => $"{{\"name\":\"{n}\",\"browser_download_url\":\"{Base}{n}\",\"size\":10}}"));
        return $"{{\"tag_name\":\"{tag}\",\"html_url\":\"https://github.com/GabrielVergara/MasterSafeAuthenticator/releases/tag/{tag}\",\"body\":\"notes\",\"assets\":[{assets}]}}";
    }

    private static UpdateChecker Checker(FakeHandler handler, string current = "1.3.2")
        => new(new HttpClient(handler), ReleaseKey.Load(), current);

    [Theory]
    [InlineData("v1.4.0", "1.3.2", true)]
    [InlineData("1.3.3", "1.3.2", true)]
    [InlineData("v1.3.2", "1.3.2", false)]
    [InlineData("v1.3.1", "1.3.2", false)]
    [InlineData("v2.0.0", "1.9.9+abc123", true)]
    [InlineData("v1.4.0", "1.4.0-beta", false)]
    public async Task ANewerVersion_IsRecognised(string tag, string current, bool newer)
    {
        var handler = new FakeHandler(_ => Ok(ReleaseJson(tag)));

        var info = await Checker(handler, current).CheckAsync();

        info.Should().NotBeNull();
        info!.IsNewer.Should().Be(newer);
        handler.Requests.Single().RequestUri!.ToString().Should().Be(UpdateChecker.LatestUrl);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"nonsense\"}")]
    [InlineData("[]")]
    public async Task UnexpectedInformation_GivesNoUpdate(string json)
    {
        var info = await Checker(new FakeHandler(_ => Ok(json))).CheckAsync();

        info.Should().BeNull();
    }

    [Fact]
    public async Task AnUnreachableServer_IsAnError_NotACrash()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var updates = new UpdatesViewModel(Checker(handler));

        var newer = await updates.CheckAsync();

        newer.Should().BeFalse();
        updates.Status.Should().Contain("Could not check");
    }

    [Fact]
    public async Task HugeReleaseInformation_IsRefused()
    {
        var handler = new FakeHandler(_ => Ok(new string('x', 2 * 1024 * 1024)));

        var act = () => Checker(handler).CheckAsync();

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task TheChecker_AsksForNothingButTheReleaseInformation()
    {
        var handler = new FakeHandler(_ => Ok(ReleaseJson("v1.4.0")));

        await Checker(handler).CheckAsync();

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public void TheRightInstallerIsChosen_ForTheSystem()
    {
        var names = new[]
        {
            "MasterSafe-Authenticator-9.9.9-win-x64-setup.exe",
            "MasterSafe-Authenticator-9.9.9-linux-x64.tar.gz",
            "MasterSafe-Authenticator-9.9.9-linux-arm64.tar.gz",
            "MasterSafe-Cli-9.9.9-win-x64.exe",
            "MasterSafe-Cli-9.9.9-linux-x64",
        };
        var info = new UpdateChecker(new HttpClient(), ReleaseKey.Load(), "1.0.0").Parse(Encoding.UTF8.GetBytes(ReleaseJson("v9.9.9", names)))!;

        UpdateChecker.PickInstaller(info, windows: true, Architecture.X64)!.Name.Should().EndWith("win-x64-setup.exe");
        UpdateChecker.PickInstaller(info, windows: false, Architecture.X64)!.Name.Should().EndWith("linux-x64.tar.gz");
        UpdateChecker.PickInstaller(info, windows: false, Architecture.Arm64)!.Name.Should().EndWith("linux-arm64.tar.gz");
        UpdateChecker.PickInstaller(info, windows: true, Architecture.X64)!.Name.Should().NotContain("Cli");
    }

    [Fact]
    public void TheBuiltInKey_IsTheOnePublishedInTheProject()
    {
        var key = ReleaseKey.Load();

        ReleaseKey.Fingerprint(key).Should().Be("28ad8deb1624d6f04fe9100e25edb2666ad904e02824af14ef2fe6c8d2a74b8a");
    }

    // ------------------------------------------------------------------ download and verify

    private (UpdateChecker Checker, UpdateInfo Info, FakeHandler Handler, byte[] Installer) Release(bool tamper = false, bool sign = true, string host = Base)
    {
        // Some systems (for example the Ubuntu build runner) cannot make ML-DSA keys. The tests that do not check the
        // signature itself still run there with placeholder keys.
        var (publicKey, seed) = ReleaseSigner.IsSupported ? ReleaseSigner.GenerateKeyPair() : ([1, 2, 3], [4, 5, 6]);
        var installer = Encoding.UTF8.GetBytes("pretend installer " + Guid.NewGuid());
        var name = OperatingSystem.IsWindows()
            ? "MasterSafe-Authenticator-9.9.9-win-x64-setup.exe"
            : RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? "MasterSafe-Authenticator-9.9.9-linux-arm64.tar.gz"
                : "MasterSafe-Authenticator-9.9.9-linux-x64.tar.gz";

        var signature = ReleaseSigner.IsSupported ? Convert.ToBase64String(ReleaseSigner.Sign(installer, seed)) : "placeholder";
        var sha = Convert.ToHexString(SHA256.HashData(installer)) + "  " + name;
        var served = tamper ? [.. installer, 1] : installer;

        var handler = new FakeHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            return url.EndsWith(".sig", StringComparison.Ordinal) ? Ok(signature)
                : url.EndsWith(".sha256", StringComparison.Ordinal) ? Ok(sha)
                : Ok(served);
        });

        var assets = sign ? new[] { name, name + ".sig", name + ".sha256" } : [name];
        var json = ReleaseJson("v9.9.9", assets).Replace(Base, host, StringComparison.Ordinal);
        var checker = new UpdateChecker(new HttpClient(handler), publicKey, "1.0.0");
        return (checker, checker.Parse(Encoding.UTF8.GetBytes(json))!, handler, installer);
    }

    [Fact]
    public async Task AGenuineDownload_IsSavedAndVerified()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var (checker, info, _, installer) = Release();

        var result = await checker.DownloadAndVerifyAsync(info, _folder);

        result.Verified.Should().BeTrue();
        File.ReadAllBytes(result.InstallerPath!).Should().Equal(installer);
        result.InstallerPath.Should().Contain("MasterSafe-update-9.9.9");
    }

    [Fact]
    public async Task ATamperedDownload_IsRejectedAndDeleted()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var (checker, info, _, _) = Release(tamper: true);

        var result = await checker.DownloadAndVerifyAsync(info, _folder);

        result.Verified.Should().BeFalse();
        result.InstallerPath.Should().BeNull();
        result.Message.Should().Contain("deleted");
        Directory.GetDirectories(_folder).Should().BeEmpty("nothing is left on disk");
    }

    [Fact]
    public async Task ADownloadSignedWithAnotherKey_IsRejected()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var (_, info, handler, _) = Release();
        var otherKey = ReleaseSigner.GenerateKeyPair().PublicKey;
        var checker = new UpdateChecker(new HttpClient(handler), otherKey, "1.0.0");

        var result = await checker.DownloadAndVerifyAsync(info, _folder);

        result.Verified.Should().BeFalse();
    }

    [Fact]
    public async Task ReleasesWithoutASignature_AreNotOffered()
    {
        var (checker, info, handler, _) = Release(sign: false);

        var result = await checker.DownloadAndVerifyAsync(info, _folder);

        result.Verified.Should().BeFalse();
        result.Message.Should().Contain("no signed download");
        handler.Requests.Should().BeEmpty("nothing was downloaded");
    }

    [Fact]
    public async Task AddressesOutsideTheProjectsReleasePage_AreNeverDownloaded()
    {
        var (checker, info, handler, _) = Release(host: "https://evil.example.com/");

        var result = await checker.DownloadAndVerifyAsync(info, _folder);

        result.Verified.Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TheUpdatesSection_ReportsAndOffersTheDownload()
    {
        var json = ReleaseJson("v9.9.9");
        var updates = new UpdatesViewModel(Checker(new FakeHandler(_ => Ok(json))), downloadFolder: () => _folder);
        UpdateInfo? found = null;
        updates.UpdateFound += (_, info) => found = info;

        var newer = await updates.CheckAsync();

        newer.Should().BeTrue();
        updates.HasUpdate.Should().BeTrue();
        updates.LatestText.Should().Contain("9.9.9");
        found.Should().NotBeNull();
        updates.HasVerifiedDownload.Should().BeFalse();
    }

    [Fact]
    public async Task WithoutAnyUpdate_TheSectionSaysSo_AndAQuietCheckSaysNothing()
    {
        var json = ReleaseJson("v0.0.1");
        var updates = new UpdatesViewModel(Checker(new FakeHandler(_ => Ok(json))));

        await updates.CheckAsync(quiet: true);
        updates.Status.Should().BeNull();

        await updates.CheckAsync();
        updates.Status.Should().Contain("newest version");
        updates.HasUpdate.Should().BeFalse();
    }

    // ------------------------------------------------------------------ breach check

    // The SHA-1 of "password" is 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8.
    private const string PasswordPrefix = "5BAA6";
    private const string PasswordSuffix = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";

    [Fact]
    public async Task OnlyTheFirstFiveCharactersOfTheHash_AreSent()
    {
        var handler = new FakeHandler(_ => Ok($"0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n{PasswordSuffix}:9545824\r\n"));

        var count = await new BreachChecker(new HttpClient(handler)).CountAsync("password");

        count.Should().Be(9545824);
        var request = handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be(BreachChecker.Endpoint + PasswordPrefix);
        request.RequestUri.AbsolutePath.Should().Be("/range/" + PasswordPrefix).And.NotContain(PasswordSuffix);
        request.Headers.GetValues("Add-Padding").Should().Contain("true");
    }

    [Fact]
    public async Task APasswordThatIsNotListed_HasCountZero_AndPaddingEntriesAreIgnored()
    {
        var handler = new FakeHandler(_ => Ok($"0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n{PasswordSuffix.ToLowerInvariant().Replace('e', 'f')}:0\r\n"));

        (await new BreachChecker(new HttpClient(handler)).CountAsync("password")).Should().Be(0);

        var padded = new FakeHandler(_ => Ok($"{PasswordSuffix}:0\r\n"));
        (await new BreachChecker(new HttpClient(padded)).CountAsync("password")).Should().Be(0);
    }

    [Fact]
    public async Task ServiceErrorsAndHugeAnswers_AreErrors()
    {
        var failing = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var act1 = () => new BreachChecker(new HttpClient(failing)).CountAsync("password");
        await act1.Should().ThrowAsync<HttpRequestException>();

        var huge = new FakeHandler(_ => Ok(new string('A', 3 * 1024 * 1024)));
        var act2 = () => new BreachChecker(new HttpClient(huge)).CountAsync("password");
        await act2.Should().ThrowAsync<HttpRequestException>();
    }

    private static PasswordEntry Entry(string title, string password) => new() { Title = title, Password = password };

    [Fact]
    public async Task TheBreachCheck_NeedsPermission_AndNeverCallsTheServiceWithoutIt()
    {
        var handler = new FakeHandler(_ => Ok(string.Empty));
        var page = new HealthViewModel(() => [Entry("A", "password")], () => false, new BreachChecker(new HttpClient(handler)));

        page.Refresh();
        page.CheckBreachesCommand.CanExecute(null).Should().BeFalse();
        await page.CheckBreachesCommand.ExecuteAsync(null);

        handler.Requests.Should().BeEmpty();
        page.BreachCheckNote.Should().Contain("off");
    }

    [Fact]
    public async Task WhenAllowed_TheBreachCheckListsBreachedPasswords_OncePerDifferentPassword()
    {
        var handler = new FakeHandler(_ => Ok($"{PasswordSuffix}:42\r\n"));
        var entries = new[] { Entry("A", "password"), Entry("B", "password"), Entry("C", "K7QF2-M9XTD-W3HPA-8RCNB") };
        var page = new HealthViewModel(() => entries, () => true, new BreachChecker(new HttpClient(handler)));
        page.Refresh();

        await page.CheckBreachesCommand.ExecuteAsync(null);

        handler.Requests.Should().HaveCount(2, "the same password is only looked up once");
        page.Breached.Select(r => r.Entry.Title).Should().BeEquivalentTo("A", "B");
        page.Status.Should().Contain("2 entries");
        page.BreachCheckDone.Should().BeTrue();

        page.ClearSecrets();

        page.Breached.Should().BeEmpty();
        page.Status.Should().BeNull();
    }

    [Fact]
    public async Task ANetworkProblem_IsShown_NotThrown()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var page = new HealthViewModel(() => [Entry("A", "password")], () => true, new BreachChecker(new HttpClient(handler)));

        await page.CheckBreachesCommand.ExecuteAsync(null);

        page.Status.Should().Contain("Could not reach");
        page.IsChecking.Should().BeFalse();
    }
}
