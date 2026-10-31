// File:    UpdatesViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// "Check for updates" in Settings. A newer version can be downloaded; the download is checked against the project's
/// signature before it is offered. The app never installs anything by itself.
/// </summary>
public sealed partial class UpdatesViewModel : ViewModelBase
{
    private readonly UpdateChecker? _checker;
    private readonly IFileDialogs? _dialogs;
    private readonly Func<string> _downloadFolder;

    /// <summary>Creates the page section.</summary>
    /// <param name="checker">The update checker; without it updates are unavailable.</param>
    /// <param name="dialogs">Used to show the downloaded file in the file manager.</param>
    /// <param name="downloadFolder">Where downloads go (the Downloads folder when not given).</param>
    public UpdatesViewModel(UpdateChecker? checker = null, IFileDialogs? dialogs = null, Func<string>? downloadFolder = null)
    {
        _checker = checker;
        _dialogs = dialogs;
        _downloadFolder = downloadFolder ?? DefaultDownloadFolder;
    }

    /// <summary>Progress and results.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>True while something is being checked or downloaded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand))]
    private bool _isBusy;

    /// <summary>The newest release, once it has been looked up.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(LatestText))]
    private UpdateInfo? _latest;

    /// <summary>Where the verified installer was saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerifiedDownload))]
    private string? _installerPath;

    /// <summary>True when a newer version exists.</summary>
    public bool HasUpdate => Latest?.IsNewer == true;

    /// <summary>True when a verified installer was saved.</summary>
    public bool HasVerifiedDownload => InstallerPath is not null;

    /// <summary>For example "Version 1.4.0 is available."</summary>
    public string LatestText => Latest is null ? string.Empty : Latest.IsNewer ? $"Version {Latest.Version} is available." : "You have the newest version.";

    /// <summary>Raised when a newer version was found; carries its text.</summary>
    public event EventHandler<UpdateInfo>? UpdateFound;

    private bool CanRun => !IsBusy && _checker is not null;

    /// <summary>Asks GitHub for the latest release. Returns true when a newer one exists.</summary>
    public async Task<bool> CheckAsync(bool quiet = false)
    {
        if (_checker is null)
        {
            return false;
        }

        IsBusy = true;
        InstallerPath = null;
        try
        {
            Latest = await _checker.CheckAsync();
            if (Latest is null)
            {
                Status = quiet ? null : "No release information was found.";
                return false;
            }

            if (Latest.IsNewer)
            {
                Status = $"Version {Latest.Version} is available. Choose Download and verify to get it.";
                UpdateFound?.Invoke(this, Latest);
                return true;
            }

            Status = quiet ? null : $"You have the newest version ({AppInfo.Version}).";
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Status = quiet ? null : $"Could not check for updates: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckAsync() => await CheckAsync(quiet: false);

    /// <summary>Downloads the installer for this system and checks its signature and checksum.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DownloadAsync()
    {
        if (_checker is null || Latest is null || !Latest.IsNewer)
        {
            return;
        }

        IsBusy = true;
        InstallerPath = null;
        Status = "Downloading and checking the signature...";
        try
        {
            var result = await _checker.DownloadAndVerifyAsync(Latest, _downloadFolder());
            if (result.Verified)
            {
                InstallerPath = result.InstallerPath;
                Status = $"Verified. The installer was saved to {result.InstallerPath}. Close MasterSafe and run it to update; your vault is not touched.";
            }
            else
            {
                Status = result.Message;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowFile()
    {
        if (InstallerPath is not null)
        {
            _dialogs?.ShowInFolder(InstallerPath);
        }
    }

    /// <summary>Opens the release page in the browser.</summary>
    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = Latest?.PageUrl;
        if (url is null || !url.StartsWith("https://github.com/GabrielVergara/MasterSafeAuthenticator/", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser available.
        }
    }

    private static string DefaultDownloadFolder()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Path.GetTempPath();
    }
}
