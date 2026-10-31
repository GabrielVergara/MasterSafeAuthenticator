// File:    AboutViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// About dialog: product name, version, and project link.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    public string ProductName => AppInfo.ProductName;

    public string Tagline => AppInfo.Tagline;

    public string Version => AppInfo.Version;

    /// <summary>Author shown in the About dialog.</summary>
    public string Author => AppInfo.Author;

    /// <summary>Copyright line.</summary>
    public string Copyright => AppInfo.Copyright;

    public string ProjectUrl => AppInfo.ProjectUrl;

    /// <summary>The author's web site.</summary>
    public string WebsiteUrl => AppInfo.WebsiteUrl;

    [RelayCommand]
    private void OpenProject() => OpenInBrowser(AppInfo.ProjectUrl);

    [RelayCommand]
    private void OpenWebsite() => OpenInBrowser(AppInfo.WebsiteUrl);

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort; ignore failures (e.g. headless environments).
        }
    }
}
