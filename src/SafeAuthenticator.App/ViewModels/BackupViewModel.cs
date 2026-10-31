// File:    BackupViewModel.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeAuthenticator.Backup;
using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>Supported backup destinations in the UI.</summary>
public enum BackupProviderKind
{
    LocalFolder,
    WebDav,
}

/// <summary>
/// Backup screen: export/import encrypted .sauth files via a local folder or WebDAV cloud provider.
/// </summary>
public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(IsWebDav))]
    private BackupProviderKind _selectedProvider = BackupProviderKind.LocalFolder;

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private string _webDavUrl = string.Empty;

    [ObservableProperty]
    private string _webDavUser = string.Empty;

    [ObservableProperty]
    private string _webDavPassword = string.Empty;

    [ObservableProperty]
    private string _masterPassword = string.Empty;

    [ObservableProperty]
    private string? _selectedBackup;

    [ObservableProperty]
    private string? _status;

    /// <summary>Supplies the accounts to export. Set by the host (e.g. the dashboard).</summary>
    public Func<IEnumerable<OtpAccount>>? AccountsProvider { get; set; }

    public IReadOnlyList<BackupProviderKind> Providers { get; } =
        [BackupProviderKind.LocalFolder, BackupProviderKind.WebDav];

    public bool IsLocal => SelectedProvider == BackupProviderKind.LocalFolder;

    public bool IsWebDav => SelectedProvider == BackupProviderKind.WebDav;

    public ObservableCollection<string> Backups { get; } = [];

    public BackupViewModel(IBackupService? backupService = null)
    {
        _backupService = backupService
            ?? new BackupService(new AesGcmEncryptionService(), new MasterPasswordService());
    }

    /// <summary>Exports the supplied accounts to the selected provider.</summary>
    public async Task<bool> ExportAsync(IEnumerable<OtpAccount> accounts)
    {
        if (!TryCreateProvider(out var provider, out var error))
        {
            Status = error;
            return false;
        }

        try
        {
            var location = await _backupService.ExportAsync(accounts, MasterPassword, provider!).ConfigureAwait(false);
            await RefreshAsync(provider).ConfigureAwait(false);
            Status = $"Backup saved: {System.IO.Path.GetFileName(location)}";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Lists available backups on the selected provider.</summary>
    public async Task RefreshAsync(IBackupProvider? provider = null)
    {
        if (provider is null && !TryCreateProvider(out provider, out var error))
        {
            Status = error;
            return;
        }

        try
        {
            var list = await provider!.ListAsync().ConfigureAwait(false);
            Backups.Clear();
            foreach (var name in list)
            {
                Backups.Add(name);
            }
        }
        catch (Exception ex)
        {
            Status = $"Could not list backups: {ex.Message}";
        }
    }

    /// <summary>Imports the selected backup and returns the restored accounts.</summary>
    public async Task<IReadOnlyList<OtpAccount>?> ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBackup))
        {
            Status = "Select a backup to import.";
            return null;
        }

        if (!TryCreateProvider(out var provider, out var error))
        {
            Status = error;
            return null;
        }

        try
        {
            var accounts = await _backupService
                .ImportAsync(SelectedBackup!, MasterPassword, provider!)
                .ConfigureAwait(false);
            Status = $"Imported {accounts.Count} account(s) from {SelectedBackup}.";
            return accounts;
        }
        catch (Exception ex)
        {
            Status = $"Import failed: {ex.Message}";
            return null;
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var accounts = AccountsProvider?.Invoke() ?? [];
        await ExportAsync(accounts).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync().ConfigureAwait(false);

    [RelayCommand]
    private async Task Import() => await ImportAsync().ConfigureAwait(false);

    private bool TryCreateProvider(out IBackupProvider? provider, out string? error)
    {
        provider = null;
        error = null;

        if (SelectedProvider == BackupProviderKind.LocalFolder)
        {
            if (string.IsNullOrWhiteSpace(Folder))
            {
                error = "Choose a backup folder first.";
                return false;
            }

            provider = new LocalFolderBackupProvider(Folder);
            return true;
        }

        if (string.IsNullOrWhiteSpace(WebDavUrl) || !Uri.TryCreate(WebDavUrl, UriKind.Absolute, out var uri))
        {
            error = "Enter a valid WebDAV collection URL.";
            return false;
        }

        var client = new HttpClient();
        if (!string.IsNullOrEmpty(WebDavUser))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{WebDavUser}:{WebDavPassword}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        provider = new WebDavBackupProvider(client, uri);
        return true;
    }
}
