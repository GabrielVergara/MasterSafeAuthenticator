// File:    AboutViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;

namespace MasterSafe.App.ViewModels;

/// <summary>A link shown in the About window.</summary>
/// <param name="Title">What it is, for example "Article".</param>
/// <param name="Description">One line about what the reader finds there.</param>
/// <param name="Url">The address. Only addresses of the project and the author are ever opened.</param>
public sealed record AboutLink(string Title, string Description, string Url);

/// <summary>
/// About dialog: product name, version, who made it, where to read more, and the fingerprint of the key that signs
/// releases so that a download can be checked.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private static readonly string[] AllowedHosts =
    [
        "github.com",
        "www.gabriel-vergara.com",
        "sitio.gabriel-vergara.com",
    ];

    public string ProductName => AppInfo.ProductName;

    public string Tagline => AppInfo.Tagline;

    public string Version => AppInfo.Version;

    /// <summary>For example "Version 1.4.0 (build ffe4d70)".</summary>
    public string VersionText => AppInfo.BuildId.Length > 0 ? $"Version {AppInfo.Version} (build {AppInfo.BuildId})" : $"Version {AppInfo.Version}";

    /// <summary>Author shown in the About dialog.</summary>
    public string Author => AppInfo.Author;

    /// <summary>Copyright line.</summary>
    public string Copyright => AppInfo.Copyright;

    /// <summary>A sentence about the licence.</summary>
    public string LicenseText => "Free for personal, non-commercial use. See the licence for the terms.";

    public string ProjectUrl => AppInfo.ProjectUrl;

    /// <summary>The author's web site.</summary>
    public string WebsiteUrl => AppInfo.WebsiteUrl;

    /// <summary>The article about MasterSafe Authenticator.</summary>
    public string ArticleUrl => AppInfo.ArticleUrl;

    /// <summary>Where to read more and where to get help.</summary>
    public IReadOnlyList<AboutLink> Links { get; } =
    [
        new("Article", "Why MasterSafe exists and how it works, on the author's site", AppInfo.ArticleUrl),
        new("Website", "Gabriel Vergara Ezcurdia", AppInfo.WebsiteUrl),
        new("Source code", "The project on GitHub", AppInfo.ProjectUrl),
        new("Releases", "Downloads, release notes and signatures", AppInfo.ReleasesUrl),
        new("Report a problem", "Questions, bugs and ideas", AppInfo.IssuesUrl),
        new("Security policy", "How to report a vulnerability", AppInfo.SecurityUrl),
        new("Licence", "The terms of use", AppInfo.LicenseUrl),
        new("Third-party notices", "Components and icons that are used, and their licences", AppInfo.NoticesUrl),
    ];

    /// <summary>The SHA-256 fingerprint of the key that signs releases, in groups for reading and comparing.</summary>
    public string SigningKeyFingerprint { get; } = ReadFingerprint();

    private static string ReadFingerprint()
    {
        try
        {
            var hex = ReleaseKey.Fingerprint(ReleaseKey.Load());
            return string.Join(' ', Enumerable.Range(0, hex.Length / 8).Select(i => hex.Substring(i * 8, 8)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>True for an https address on a host that belongs to the project or its author.</summary>
    public static bool IsAllowedLink(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && string.IsNullOrEmpty(uri.UserInfo)
           && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
           && (uri.Host != "github.com" || uri.AbsolutePath.StartsWith("/GabrielVergara/", StringComparison.Ordinal));

    [RelayCommand]
    private void OpenProject() => OpenInBrowser(AppInfo.ProjectUrl);

    [RelayCommand]
    private void OpenWebsite() => OpenInBrowser(AppInfo.WebsiteUrl);

    [RelayCommand]
    private void OpenArticle() => OpenInBrowser(AppInfo.ArticleUrl);

    /// <summary>Opens one of <see cref="Links"/> in the browser.</summary>
    [RelayCommand]
    private void OpenLink(AboutLink? link)
    {
        if (link is not null)
        {
            OpenInBrowser(link.Url);
        }
    }

    /// <summary>Replaced in tests; by default it uses the system browser.</summary>
    public Action<string> UrlOpener { get; set; } = StartBrowser;

    private void OpenInBrowser(string url)
    {
        if (IsAllowedLink(url))
        {
            UrlOpener(url);
        }
    }

    private static void StartBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Opening a browser is best-effort; ignore failures (for example without a desktop).
        }
    }
}
