// File:    KeePassImportViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Core.Import;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Imports passwords and authenticator accounts from a KeePass or KeePassXC export. The file is read first and shown as
/// a preview; nothing is added to the vault until the user confirms.
/// </summary>
public sealed partial class KeePassImportViewModel : ViewModelBase
{
    private KeePassImportResult? _result;
    private List<PasswordEntry> _newPasswords = [];
    private List<OtpAccount> _newAccounts = [];

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _hasPreview;

    /// <summary>True while a KeePass database waits for its password or key file.</summary>
    [ObservableProperty]
    private bool _needsDatabaseKey;

    /// <summary>The KeePass database's password. Cleared as soon as it has been used.</summary>
    [ObservableProperty]
    private string _databasePassword = string.Empty;

    /// <summary>The KeePass key file, if the database uses one.</summary>
    [ObservableProperty]
    private string? _keyFilePath;

    /// <summary>True while the database is being opened, which can take a few seconds.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDatabaseCommand))]
    private bool _isOpening;

    /// <summary>Lines of the preview: what was found and what will be skipped.</summary>
    public ObservableCollection<string> Preview { get; } = [];

    /// <summary>File pickers; set by the shell.</summary>
    public IFileDialogs? Dialogs { get; set; }

    /// <summary>The password entries already in the vault, used to skip duplicates; set by the shell.</summary>
    public Func<IEnumerable<PasswordEntry>>? ExistingPasswords { get; set; }

    /// <summary>The accounts already in the vault, used to skip duplicates; set by the shell.</summary>
    public Func<IEnumerable<OtpAccount>>? ExistingAccounts { get; set; }

    /// <summary>Raised when the user confirms; carries only the entries that are not already in the vault.</summary>
    public event EventHandler<KeePassImportConfirmedEventArgs>? Confirmed;

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.OpenFileAsync("Choose an export file", "Export files", "*.kdbx", "*.xml", "*.csv", "*.json", "*.1pux", "*.2fas", "*.zip", "*.txt");
        if (path is not null)
        {
            Load(path);
        }
    }

    /// <summary>Chooses the key file of a KeePass database.</summary>
    [RelayCommand]
    private async Task ChooseKeyFileAsync()
    {
        if (Dialogs is not null && await Dialogs.OpenFileAsync("Choose the database's key file", "Key files", "*.keyx", "*.key", "*.*") is { } path)
        {
            KeyFilePath = path;
        }
    }

    /// <summary>Opens the KeePass database with the password and key file given, then shows the preview.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenDatabase))]
    private async Task OpenDatabaseAsync()
    {
        if (FilePath is null)
        {
            return;
        }

        IsOpening = true;
        Status = "Opening the KeePass database. This can take a few seconds.";
        var path = FilePath;
        var password = DatabasePassword;
        DatabasePassword = string.Empty;
        try
        {
            if (new FileInfo(path).Length > MaxDatabaseBytes || (KeyFilePath is not null && new FileInfo(KeyFilePath).Length > 1024 * 1024))
            {
                throw new KeePassImportException($"The database is larger than {MaxDatabaseBytes / (1024 * 1024)} MiB, or the key file larger than 1 MiB, which is more than MasterSafe reads.");
            }

            var keyFile = KeyFilePath is null ? null : await File.ReadAllBytesAsync(KeyFilePath);
            var file = await File.ReadAllBytesAsync(path);
            var xml = await Task.Run(() => KdbxReader.ReadXml(file, password, keyFile));
            var result = await Task.Run(() => KeePassImporter.ParseXml(xml));
            NeedsDatabaseKey = false;
            KeyFilePath = null;
            ShowPreview(path, result);
        }
        catch (KeePassImportException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"The file could not be read: {ex.Message}";
        }
        finally
        {
            IsOpening = false;
        }
    }

    private bool CanOpenDatabase() => !IsOpening;

    /// <summary>Largest KeePass database that is opened.</summary>
    public const long MaxDatabaseBytes = 64L * 1024 * 1024;

    /// <summary>Reads an export and builds the preview. Returns true when there is something to import.</summary>
    public bool Load(string path)
    {
        Discard();
        NeedsDatabaseKey = false;
        KeyFilePath = null;
        try
        {
            if (IsDatabase(path))
            {
                // A KeePass database is encrypted: ask for its password first. Nothing is read until then.
                FilePath = path;
                NeedsDatabaseKey = true;
                Status = "This is a KeePass database. Enter its password, and choose its key file if it has one, then choose Open.";
                return false;
            }

            return ShowPreview(path, KeePassImporter.ReadFile(path));
        }
        catch (KeePassImportException ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    private static bool IsDatabase(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> start = stackalloc byte[8];
            return stream.ReadAtLeast(start, 8, throwOnEndOfStream: false) == 8 && KdbxReader.IsKdbx(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KeePassImportException($"The file could not be read: {ex.Message}");
        }
    }

    private bool ShowPreview(string path, KeePassImportResult result)
    {
        _result = result;
        FilePath = path;
        var knownPasswords = (ExistingPasswords?.Invoke() ?? []).Select(PasswordKey).ToHashSet(StringComparer.Ordinal);
        var knownAccounts = (ExistingAccounts?.Invoke() ?? []).Select(AccountKey).ToHashSet(StringComparer.Ordinal);

        // Add() also drops duplicates inside the file itself.
        _newPasswords = _result.Passwords.Where(p => knownPasswords.Add(PasswordKey(p))).ToList();
        _newAccounts = _result.Accounts.Where(a => knownAccounts.Add(AccountKey(a))).ToList();
        var duplicates = (_result.Passwords.Count - _newPasswords.Count) + (_result.Accounts.Count - _newAccounts.Count);

        Preview.Add($"Passwords to add: {_newPasswords.Count}");
        Preview.Add($"Authenticator accounts to add: {_newAccounts.Count}");
        AddIfAny($"Already in your vault, skipped: {duplicates}", duplicates);
        AddIfAny($"Entries in the recycle bin, skipped: {_result.SkippedRecycleBin}", _result.SkippedRecycleBin);
        AddIfAny($"Old versions of entries, skipped: {_result.SkippedHistory}", _result.SkippedHistory);
        AddIfAny(
            $"Attached files to add: {_newPasswords.Sum(p => p.Attachments.Count)} ({AttachmentLimits.Describe(_newPasswords.SelectMany(p => p.Attachments).Sum(a => a.Size))})",
            _newPasswords.Sum(p => p.Attachments.Count));
        AddIfAny(
            $"Attached files not imported (over {AttachmentLimits.Describe(AttachmentLimits.MaxFileBytes)} each, or over the vault limit): {_result.SkippedAttachments}",
            _result.SkippedAttachments);
        AddIfAny($"Entries without a title or password, skipped: {_result.SkippedInvalid}", _result.SkippedInvalid);
        foreach (var warning in _result.Warnings.Take(10))
        {
            Preview.Add(warning);
        }

        if (_newPasswords.Count == 0 && _newAccounts.Count == 0)
        {
            Status = _result.TotalEntries == 0 ? "The file has no entries." : "Nothing new to import: everything in the file is already in your vault or was skipped.";
            HasPreview = true;
            return false;
        }

        Status = "Check the summary, then choose Import to add these to your vault. Nothing has been added yet.";
        HasPreview = true;
        return true;
    }

    /// <summary>Adds the previewed entries to the vault.</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (!HasPreview || (_newPasswords.Count == 0 && _newAccounts.Count == 0))
        {
            return;
        }

        var passwords = _newPasswords;
        var accounts = _newAccounts;
        var wanted = passwords.SelectMany(p => p.Attachments).Select(a => a.Id).ToHashSet();
        var attachments = _result!.Attachments.Where(a => wanted.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value);
        Discard();
        Status = $"Imported {passwords.Count} passwords and {accounts.Count} authenticator accounts.";
        Confirmed?.Invoke(this, new KeePassImportConfirmedEventArgs(passwords, accounts, attachments));
    }

    /// <summary>Throws the preview away without importing.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Discard();
        Status = "Import cancelled. Nothing was added.";
    }

    /// <summary>Shows the export file in the file manager so it can be deleted after importing.</summary>
    [RelayCommand]
    private void ShowFile()
    {
        if (FilePath is not null)
        {
            Dialogs?.ShowInFolder(FilePath);
        }
    }

    /// <summary>Forgets everything read from the file.</summary>
    public void Discard()
    {
        _result = null;
        _newPasswords = [];
        _newAccounts = [];
        Preview.Clear();
        HasPreview = false;
    }

    private static string PasswordKey(PasswordEntry e)
        => string.Join('', e.Title.ToLowerInvariant(), e.UserName.ToLowerInvariant(), e.Url.ToLowerInvariant(), e.Password);

    private static string AccountKey(OtpAccount a)
        => string.Join(
            '',
            a.Secret.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            a.Issuer.ToLowerInvariant(),
            a.Label.ToLowerInvariant());

    private void AddIfAny(string line, int count)
    {
        if (count > 0)
        {
            Preview.Add(line);
        }
    }
}

/// <summary>The entries to add after the user confirmed a KeePass import.</summary>
public sealed class KeePassImportConfirmedEventArgs(
    IReadOnlyList<PasswordEntry> passwords,
    IReadOnlyList<OtpAccount> accounts,
    IReadOnlyDictionary<Guid, byte[]> attachments) : EventArgs
{
    /// <summary>The bytes of the attached files, by attachment id.</summary>
    public IReadOnlyDictionary<Guid, byte[]> Attachments { get; } = attachments;

    /// <summary>Password entries to add.</summary>
    public IReadOnlyList<PasswordEntry> Passwords { get; } = passwords;

    /// <summary>Authenticator accounts to add.</summary>
    public IReadOnlyList<OtpAccount> Accounts { get; } = accounts;
}
