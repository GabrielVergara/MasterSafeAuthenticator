// File:    UpdateChecker.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterSafe.PostQuantum;

namespace MasterSafe.App.Services;

/// <summary>A file attached to a release.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>What the latest release on GitHub looks like.</summary>
public sealed record UpdateInfo(string Tag, Version Version, string PageUrl, string Notes, IReadOnlyList<ReleaseAsset> Assets, bool IsNewer);

/// <summary>The outcome of downloading and checking an update.</summary>
public sealed record UpdateDownload(bool Verified, string? InstallerPath, string Message);

/// <summary>The project's release signing key, which is built into the app so an update can be checked without trusting the download.</summary>
public static class ReleaseKey
{
    /// <summary>The ML-DSA-65 public key.</summary>
    public static byte[] Load()
    {
        using var stream = typeof(ReleaseKey).Assembly.GetManifestResourceStream("masterSafe-release.pub")
            ?? throw new InvalidOperationException("The release key is missing from this build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Convert.FromBase64String(reader.ReadToEnd().Trim());
    }

    /// <summary>The key's SHA-256 fingerprint, the same value that is published in SECURITY.md and the README.</summary>
    public static string Fingerprint(byte[] key) => Convert.ToHexString(SHA256.HashData(key)).ToLowerInvariant();
}

/// <summary>
/// Looks for a newer release on GitHub and, when asked, downloads it and checks its ML-DSA signature and checksum against
/// the key built into the app. Nothing is installed by the app: a verified installer is only saved, and the user runs it.
/// <para>
/// It only talks to github.com on the user's request or, when the user turned it on, once after unlocking. No information
/// about the user or the vault is sent.
/// </para>
/// </summary>
public sealed class UpdateChecker(HttpClient http, byte[] publicKey, string currentVersion)
{
    /// <summary>The release information address.</summary>
    public const string LatestUrl = "https://api.github.com/repos/GabrielVergara/MasterSafeAuthenticator/releases/latest";

    private const string DownloadPrefix = "https://github.com/GabrielVergara/MasterSafeAuthenticator/releases/download/";
    private const long MaxInfoBytes = 1024 * 1024;
    private const long MaxInstallerBytes = 150L * 1024 * 1024;
    private const long MaxSmallFileBytes = 64 * 1024;

    /// <summary>Creates a client with a timeout and an honest user agent.</summary>
    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MasterSafe-Authenticator", AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Asks GitHub for the latest release. Null when there is none.</summary>
    /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await ReadLimitedAsync(LatestUrl, MaxInfoBytes, cancellationToken).ConfigureAwait(false);
        return Parse(bytes);
    }

    /// <summary>Reads the JSON that GitHub returns for a release.</summary>
    public UpdateInfo? Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tag)
        {
            return null;
        }

        if (!TryParseVersion(tag, out var latest))
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in list.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                if (name is not null && url is not null)
                {
                    assets.Add(new ReleaseAsset(name, url, size));
                }
            }
        }

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? string.Empty : string.Empty;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
        var isNewer = TryParseVersion(currentVersion, out var current) && latest > current;
        return new UpdateInfo(tag, latest, page, notes, assets, isNewer);
    }

    /// <summary>Reads "v1.4.0", "1.4.0" or "1.4.0+abc" as a version.</summary>
    public static bool TryParseVersion(string text, out Version version)
    {
        var clean = text.Trim().TrimStart('v', 'V');
        var cut = clean.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0)
        {
            clean = clean[..cut];
        }

        return Version.TryParse(clean, out version!);
    }

    /// <summary>Chooses the installer or package for this system: the Windows installer, or the Linux package for this processor.</summary>
    public static ReleaseAsset? PickInstaller(UpdateInfo info, bool windows, Architecture architecture)
    {
        var suffix = windows
            ? "-win-x64-setup.exe"
            : architecture == Architecture.Arm64 ? "-linux-arm64.tar.gz" : "-linux-x64.tar.gz";
        return info.Assets.FirstOrDefault(a => a.Name.StartsWith("MasterSafe-Authenticator-", StringComparison.Ordinal)
                                               && a.Name.EndsWith(suffix, StringComparison.Ordinal)
                                               && IsPlainFileName(a.Name));
    }

    // The name comes from the server and becomes a path on this computer, so it must not be able to leave the folder.
    private static bool IsPlainFileName(string name)
        => !name.Contains("..", StringComparison.Ordinal) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>
    /// Downloads the installer for this system with its signature and checksum into a new folder under
    /// <paramref name="folder"/>, and checks them against the built-in key. A file that does not verify is deleted.
    /// </summary>
    public async Task<UpdateDownload> DownloadAndVerifyAsync(UpdateInfo info, string folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        var installer = PickInstaller(info, OperatingSystem.IsWindows(), RuntimeInformation.OSArchitecture);
        var signature = installer is null ? null : info.Assets.FirstOrDefault(a => a.Name == installer.Name + ".sig");
        var checksum = installer is null ? null : info.Assets.FirstOrDefault(a => a.Name == installer.Name + ".sha256");
        if (installer is null || signature is null || checksum is null)
        {
            return new UpdateDownload(false, null, "This release has no signed download for this system. Open the release page instead.");
        }

        foreach (var asset in new[] { installer, signature, checksum })
        {
            if (!asset.Url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
            {
                return new UpdateDownload(false, null, "The download address is not on the project's GitHub release page, so it was not used.");
            }
        }

        var target = Path.Combine(folder, $"MasterSafe-update-{info.Version}");
        Directory.CreateDirectory(target);
        var installerPath = Path.Combine(target, installer.Name);

        try
        {
            await DownloadAsync(installer.Url, installerPath, MaxInstallerBytes, cancellationToken).ConfigureAwait(false);
            await DownloadAsync(signature.Url, installerPath + ".sig", MaxSmallFileBytes, cancellationToken).ConfigureAwait(false);
            await DownloadAsync(checksum.Url, installerPath + ".sha256", MaxSmallFileBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Cleanup(target);
            return new UpdateDownload(false, null, $"The download failed: {ex.Message}");
        }

        if (!ReleaseSigner.IsSupported)
        {
            Cleanup(target);
            return new UpdateDownload(false, null, "This system cannot check the signature, so the download was deleted.");
        }

        var result = ReleaseVerifier.VerifyFile(installerPath, publicKey);
        if (!result.Passed)
        {
            Cleanup(target);
            return new UpdateDownload(false, null, "The download did not match the project's signature or checksum, so it was deleted. Do not install it.");
        }

        return new UpdateDownload(true, installerPath, "The signature and checksum are valid.");
    }

    private async Task<byte[]> ReadLimitedAsync(string url, long limit, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await CopyLimitedAsync(stream, memory, limit, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private async Task DownloadAsync(string url, string path, long limit, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(path);
        await CopyLimitedAsync(stream, file, limit, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new InvalidDataException("The file is larger than expected.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Cleanup(string folder)
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; the user can delete it by hand.
        }
    }
}
