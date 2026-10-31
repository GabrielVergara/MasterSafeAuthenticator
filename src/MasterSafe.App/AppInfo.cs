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
    public const string Tagline = "A privacy-first, offline-first authenticator for TOTP/HOTP.";

    /// <summary>Project home page.</summary>
    public const string ProjectUrl = "https://github.com/gabrielezcurdia-lgtm/MasterSafe";

    /// <summary>Author of the application.</summary>
    public const string Author = "Gabriel Vergara Ezcurdia";

    /// <summary>Byline shown under the product name on the splash window.</summary>
    public static string SplashByline => $"by {Author}";

    /// <summary>Version line shown at the bottom of the splash window.</summary>
    public static string SplashVersion => $"Version {Version}";

    /// <summary>Copyright line.</summary>
    public static string Copyright => $"© {DateTime.Now.Year} {Author}";

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
