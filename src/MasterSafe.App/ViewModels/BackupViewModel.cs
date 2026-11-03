// File:    BackupViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

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

    /// <summary>Supplies everything in the vault. Set by the shell.</summary>
    public Func<VaultContents>? ContentsProvider { get; set; }

    /// <summary>Joins a backup into the vault and returns what to show. Set by the shell.</summary>
    public Func<VaultContents, Task<string>>? MergeRequested { get; set; }

    /// <summary>File pickers. Set by the shell.</summary>
    public IFileDialogs? Dialogs { get; set; }

    /// <summary>Sync between computers.</summary>
    public SyncViewModel Sync { get; } = new();

    /// <summary>True while a KeePass file is being written.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportKdbxCommand))]
    private bool _isWorking;

    private bool CanExportKdbx => !IsWorking;

    private bool PasswordIsLongEnough()
    {
        if (MasterPassword.Length >= LoginViewModel.MinPasswordLength)
        {
            return true;
        }

        Status = $"Choose a password of at least {LoginViewModel.MinPasswordLength} characters. It protects the file wherever it goes.";
        return false;
    }

    /// <summary>Exports everything in the vault: accounts, passwords and attached files.</summary>
    public async Task<bool> ExportAllAsync()
    {
        if (!PasswordIsLongEnough() || ContentsProvider is null)
        {
            return false;
        }

        if (!TryCreateProvider(out var provider, out var error))
        {
            Status = error;
            return false;
        }

        try
        {
            var location = await _backupService.ExportContentsAsync(ContentsProvider(), MasterPassword, provider!);
            await RefreshAsync(provider);
            Status = $"Backup saved: {System.IO.Path.GetFileName(location)}";
            return true;
        }
        catch (Exception ex) when (ex is BackupProviderException or BackupNameTakenException or HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Status = $"Export failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Writes a KeePass database, protected by the password in the form, to a file the user chooses.</summary>
    [RelayCommand(CanExecute = nameof(CanExportKdbx))]
    private async Task ExportKdbxAsync()
    {
        if (!PasswordIsLongEnough() || ContentsProvider is null || Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.SaveFileAsync("Save the KeePass database", "MasterSafe-export.kdbx", "kdbx");
        if (path is null)
        {
            return;
        }

        IsWorking = true;
        Status = "Writing the KeePass database...";
        try
        {
            var contents = ContentsProvider();
            var password = MasterPassword;
            await File.WriteAllBytesAsync(path, await Task.Run(() => KdbxWriter.Write(contents, password)));
            Status = $"Saved {System.IO.Path.GetFileName(path)}. It opens in KeePass and KeePassXC with the password you typed. Recycle bin items and earlier versions are not included.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

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
        if (ContentsProvider is not null)
        {
            await ExportAllAsync();
            return;
        }

        var accounts = AccountsProvider?.Invoke() ?? [];
        await ExportAsync(accounts).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync().ConfigureAwait(false);

    /// <summary>Reads the selected backup and joins it into the vault. Nothing in the vault is replaced.</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (MergeRequested is null)
        {
            await ImportAsync().ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedBackup))
        {
            Status = "Select a backup to import.";
            return;
        }

        if (!TryCreateProvider(out var provider, out var error))
        {
            Status = error;
            return;
        }

        try
        {
            var contents = await _backupService.ImportContentsAsync(SelectedBackup!, MasterPassword, provider!);
            Status = await MergeRequested(contents);
        }
        catch (InvalidPasswordException)
        {
            Status = "That password does not open this backup.";
        }
        catch (Exception ex) when (ex is InvalidDataException or BackupProviderException or HttpRequestException or IOException or UnauthorizedAccessException or FormatException or TaskCanceledException)
        {
            Status = $"Import failed: {ex.Message}";
        }
    }

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

        var client = WebDavBackupProvider.CreateClient(WebDavUser, WebDavPassword);
        try
        {
            provider = new WebDavBackupProvider(client, uri);
        }
        catch (ArgumentException ex)
        {
            client.Dispose();
            error = ex.Message.Split(" (Parameter", 2)[0];
            return false;
        }

        return true;
    }
}
