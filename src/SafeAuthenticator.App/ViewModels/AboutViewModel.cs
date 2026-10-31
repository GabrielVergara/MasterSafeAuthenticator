using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// About dialog: product name, version, and project link.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    public string ProductName => AppInfo.ProductName;

    public string Tagline => AppInfo.Tagline;

    public string Version => AppInfo.Version;

    public string Copyright => AppInfo.Copyright;

    public string ProjectUrl => AppInfo.ProjectUrl;

    [RelayCommand]
    private void OpenProject()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppInfo.ProjectUrl) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort; ignore failures (e.g. headless environments).
        }
    }
}
