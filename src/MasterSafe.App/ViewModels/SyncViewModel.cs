// File:    SyncViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Sync between computers through a folder or a WebDAV server of the user's own. It is off until the user sets it up, and
/// nothing leaves the computer before that. The settings are kept inside the vault, not in the settings file.
/// </summary>
public sealed partial class SyncViewModel : ViewModelBase
{
    /// <summary>From this many files in the place, the page suggests removing old ones by hand.</summary>
    public const int ManyFiles = 300;

    /// <summary>The settings in use; null when sync is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured), nameof(IsNotConfigured), nameof(PlaceText), nameof(LastSyncText))]
    private SyncSettings? _settings;

    /// <summary>True when sync is set up.</summary>
    public bool IsConfigured => Settings is not null;

    /// <summary>True when sync is not set up.</summary>
    public bool IsNotConfigured => Settings is null;

    /// <summary>True to use a WebDAV server, false to use a folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseFolder))]
    private bool _useWebDav;

    /// <summary>True to use a folder.</summary>
    public bool UseFolder => !UseWebDav;

    /// <summary>The folder sync files go to.</summary>
    [ObservableProperty]
    private string _folder = string.Empty;

    /// <summary>The WebDAV folder address.</summary>
    [ObservableProperty]
    private string _webDavUrl = string.Empty;

    /// <summary>The WebDAV user name.</summary>
    [ObservableProperty]
    private string _webDavUser = string.Empty;

    /// <summary>The WebDAV password.</summary>
    [ObservableProperty]
    private string _webDavPassword = string.Empty;

    /// <summary>The code typed on a second computer.</summary>
    [ObservableProperty]
    private string _joinCode = string.Empty;

    /// <summary>The sync code, while the user asked to see it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCodeShown))]
    private string? _shownCode;

    /// <summary>True while the sync code is on screen.</summary>
    public bool IsCodeShown => ShownCode is not null;

    /// <summary>What happened last.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>True while a sync runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand), nameof(StartCommand), nameof(JoinCommand))]
    private bool _isBusy;

    /// <summary>Sync after unlocking and a short time after a change.</summary>
    [ObservableProperty]
    private bool _automatic;

    /// <summary>File pickers. Set by the shell.</summary>
    public IFileDialogs? Dialogs { get; set; }

    /// <summary>Runs a sync with these settings and returns what to show; the bool is true when the settings can be kept. Set by the shell.</summary>
    public Func<SyncSettings, bool, Task<(bool Keep, string Message)>>? RunRequested { get; set; }

    /// <summary>Stores new settings in the vault, or none. Set by the shell.</summary>
    public Func<SyncSettings?, Task>? SettingsChanged { get; set; }

    /// <summary>Where sync files go.</summary>
    public string PlaceText => Settings is null ? string.Empty : Settings.UseWebDav ? Settings.WebDavUrl : Settings.Folder;

    /// <summary>When sync last ran.</summary>
    public string LastSyncText => Settings?.LastSyncUtc is { } when
        ? $"Last sync: {when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
        : "Not synced yet.";

    partial void OnSettingsChanged(SyncSettings? value)
    {
        _loading = true;
        Automatic = value?.Automatic == true;
        _loading = false;
    }

    private bool _loading;

    partial void OnAutomaticChanged(bool value)
    {
        if (!_loading && Settings is not null)
        {
            Settings = Settings with { Automatic = value };
            _ = SettingsChanged?.Invoke(Settings);
        }
    }

    /// <summary>Creates the connection for a set of settings. The caller owns nothing that needs disposing beyond the run.</summary>
    public static IBackupProvider CreateProvider(SyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.UseWebDav
            ? new WebDavBackupProvider(WebDavBackupProvider.CreateClient(settings.WebDavUser, settings.WebDavPassword), new Uri(settings.WebDavUrl))
            : new LocalFolderBackupProvider(settings.Folder);
    }

    /// <summary>Reads what the form says, or null and the reason.</summary>
    private SyncSettings? SettingsFromForm(byte[] key)
    {
        if (UseWebDav)
        {
            if (!Uri.TryCreate(WebDavUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                Status = "Enter the WebDAV folder address. It must start with https://.";
                return null;
            }
        }
        else if (string.IsNullOrWhiteSpace(Folder))
        {
            Status = "Choose a folder first.";
            return null;
        }

        return new SyncSettings
        {
            UseWebDav = UseWebDav,
            Folder = Folder.Trim(),
            WebDavUrl = WebDavUrl.Trim(),
            WebDavUser = WebDavUser.Trim(),
            WebDavPassword = WebDavPassword,
            Key = Convert.ToBase64String(key),
            Automatic = false,
        };
    }

    private bool CanRun => !IsBusy;

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (Dialogs is not null && await Dialogs.PickFolderAsync("Choose a folder for sync files") is { } folder)
        {
            Folder = folder;
        }
    }

    /// <summary>Starts a new sync from this computer. A new random key is made.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task StartAsync()
    {
        var settings = SettingsFromForm(SyncCode.NewKey());
        if (settings is not null)
        {
            await RunAsync(settings, joining: false);
            if (IsConfigured)
            {
                ShowCode();
            }
        }
    }

    /// <summary>Joins a sync that was started on another computer.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task JoinAsync()
    {
        var key = SyncCode.TryParse(JoinCode);
        if (key is null)
        {
            Status = "That sync code is not right. Check it and type it again.";
            return;
        }

        var settings = SettingsFromForm(key);
        if (settings is not null)
        {
            await RunAsync(settings, joining: true);
        }
    }

    /// <summary>Syncs now.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SyncNowAsync()
    {
        if (Settings is not null)
        {
            await RunAsync(Settings, joining: false);
        }
    }

    private async Task RunAsync(SyncSettings settings, bool joining)
    {
        if (RunRequested is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Syncing...";
        try
        {
            var (keep, message) = await RunRequested(settings, joining);
            Status = message;
            if (keep)
            {
                JoinCode = WebDavPassword = string.Empty;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Shows the sync code, to type on another computer.</summary>
    [RelayCommand]
    private void ShowCode()
    {
        if (Settings is not null)
        {
            ShownCode = SyncCode.Format(Convert.FromBase64String(Settings.Key));
        }
    }

    /// <summary>Hides the sync code.</summary>
    [RelayCommand]
    private void HideCode() => ShownCode = null;

    /// <summary>Stops syncing on this computer. The files in the folder or on the server stay there.</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        Settings = null;
        ShownCode = null;
        if (SettingsChanged is not null)
        {
            await SettingsChanged(null);
        }

        Status = "Sync is off on this computer. The sync files were left where they are.";
    }

    /// <summary>Forgets everything on screen (used when the vault is locked).</summary>
    public void ClearSecrets()
    {
        Settings = null;
        ShownCode = null;
        JoinCode = WebDavPassword = string.Empty;
        Status = null;
    }
}
