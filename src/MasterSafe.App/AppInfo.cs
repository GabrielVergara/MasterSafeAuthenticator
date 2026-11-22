// File:    AppInfo.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Reflection;

namespace MasterSafe.App;

/// <summary>Static product metadata shown in the About window and Help pages.</summary>
public static class AppInfo
{
    /// <summary>Display name of the product.</summary>
    public const string ProductName = "MasterSafe Authenticator";

    /// <summary>Short tagline.</summary>
    public const string Tagline = "Authenticator codes and passwords in one encrypted vault. Private, offline-first and built to be verified.";

    /// <summary>Project home page.</summary>
    public const string ProjectUrl = "https://github.com/GabrielVergara/MasterSafeAuthenticator";

    /// <summary>The article about MasterSafe Authenticator on the author's site.</summary>
    public const string ArticleUrl = "https://www.gabriel-vergara.com/articles/mastersafe-authenticator/";

    /// <summary>The releases (downloads and release notes).</summary>
    public const string ReleasesUrl = "https://github.com/GabrielVergara/MasterSafeAuthenticator/releases";

    /// <summary>The licence text.</summary>
    public const string LicenseUrl = "https://github.com/GabrielVergara/MasterSafeAuthenticator/blob/main/LICENSE";

    /// <summary>The list of third-party components and their licences.</summary>
    public const string NoticesUrl = "https://github.com/GabrielVergara/MasterSafeAuthenticator/blob/main/THIRD-PARTY-NOTICES.md";

    /// <summary>Where problems and questions are reported.</summary>
    public const string IssuesUrl = "https://github.com/GabrielVergara/MasterSafeAuthenticator/issues";

    /// <summary>The security policy (how to report a vulnerability).</summary>
    public const string SecurityUrl = "https://github.com/GabrielVergara/MasterSafeAuthenticator/security/policy";

    /// <summary>The page about donating to MasterSafe, on the author's site. It links on to PayPal.</summary>
    public const string SupportUrl = "https://www.gabriel-vergara.com/support-mastersafe/";

    /// <summary>The user manual on the author's site, in the language the app shows (English for anything else).</summary>
    public static string ManualUrl(string language) => language switch
    {
        "es" or "fi" => $"https://www.gabriel-vergara.com/{language}/mastersafe-manual/",
        _ => "https://www.gabriel-vergara.com/mastersafe-manual/",
    };

    /// <summary>The author's web site.</summary>
    public const string WebsiteUrl = "https://www.gabriel-vergara.com/";

    /// <summary>
    /// The store that installs and updates this copy, or null when MasterSafe updates itself. Inside a Flatpak
    /// (the <c>FLATPAK_ID</c> variable is set) updates come from Flathub, and inside an MSIX package from the Microsoft
    /// Store; both stores forbid an app updating itself.
    /// </summary>
    public static string? UpdatedBy { get; } = StoreFor(Environment.GetEnvironmentVariable("FLATPAK_ID"), IsPackaged());

    /// <summary>The store for a given <c>FLATPAK_ID</c> value and MSIX state. Public for tests.</summary>
    public static string? StoreFor(string? flatpakId, bool msixPackaged = false)
        => !string.IsNullOrWhiteSpace(flatpakId) ? "Flathub" : msixPackaged ? MicrosoftStore : null;

    /// <summary>The name shown when the Microsoft Store updates this copy.</summary>
    public const string MicrosoftStore = "Microsoft Store";

    // A process started from an MSIX package has a package identity; any other gets APPMODEL_ERROR_NO_PACKAGE (15700).
    private static bool IsPackaged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != 15700;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    /// <summary>Author of the application.</summary>
    public const string Author = "Gabriel Vergara Ezcurdia";

    /// <summary>Byline shown under the product name on the splash window.</summary>
    public static string SplashByline => $"by {Author}";

    /// <summary>Version line shown at the bottom of the splash window.</summary>
    public static string SplashVersion => $"Version {Version}";

    /// <summary>Copyright line.</summary>
    public static string Copyright => $"© {DateTime.Now.Year} {Author}";

    /// <summary>The short id of the source commit this build was made from (for example "ffe4d70"), or an empty string.</summary>
    public static string BuildId
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var plus = informational?.IndexOf('+') ?? -1;
            return plus >= 0 && informational!.Length > plus + 1 ? informational[(plus + 1)..][..Math.Min(7, informational.Length - plus - 1)] : string.Empty;
        }
    }

    /// <summary>The informational or assembly version of the running application.</summary>
    public static string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Strip build metadata (e.g. "1.0.0+abc123") for display.
                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }
    }
}
