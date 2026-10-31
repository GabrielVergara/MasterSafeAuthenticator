// File:    PasswordsViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// KeePass-style password manager page: searchable list plus an add/edit form with a generator.
/// </summary>
public sealed partial class PasswordsViewModel : ViewModelBase
{
    private readonly List<PasswordEntry> _all = [];
    private Guid? _editingId;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private string? _status;

    /// <summary>Entries matching the current search.</summary>
    public ObservableCollection<PasswordEntry> Entries { get; } = [];

    /// <summary>The same entries as rows with a check box, which is what the page shows.</summary>
    public ObservableCollection<PasswordRow> Rows { get; } = [];

    private readonly HashSet<Guid> _selected = [];

    /// <summary>How many of the shown entries are ticked.</summary>
    public int SelectedCount => _selected.Count;

    /// <summary>True when at least one entry is ticked.</summary>
    public bool HasSelection => _selected.Count > 0;

    /// <summary>True while deleting the ticked entries waits for confirmation.</summary>
    [ObservableProperty]
    private bool _isConfirmingBulkDelete;

    /// <summary>Question shown while deleting the ticked entries waits for confirmation.</summary>
    public string BulkDeleteQuestion => $"Delete {SelectedCount} selected {(SelectedCount == 1 ? "password" : "passwords")}? This cannot be undone.";

    /// <summary>Ticks all the entries that are currently shown.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = true;
        }
    }

    /// <summary>Unticks everything.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }

        _selected.Clear();
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void RequestDeleteSelected()
    {
        if (HasSelection)
        {
            PendingDelete = null;
            IsConfirmingBulkDelete = true;
            OnPropertyChanged(nameof(BulkDeleteQuestion));
        }
    }

    [RelayCommand]
    private void CancelDeleteSelected() => IsConfirmingBulkDelete = false;

    [RelayCommand]
    private void ConfirmDeleteSelected()
    {
        IsConfirmingBulkDelete = false;
        var ids = _selected.ToHashSet();
        var removed = _all.RemoveAll(e => ids.Contains(e.Id));
        _selected.Clear();
        if (removed == 0)
        {
            NotifySelectionChanged();
            return;
        }

        Status = $"Deleted {removed} {(removed == 1 ? "password" : "passwords")}.";
        PruneBlobs();
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectionChanged(PasswordRow row)
    {
        if (row.IsSelected)
        {
            _selected.Add(row.Entry.Id);
        }
        else
        {
            _selected.Remove(row.Entry.Id);
        }

        if (_selected.Count == 0)
        {
            IsConfirmingBulkDelete = false;
        }

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(BulkDeleteQuestion));
    }

    /// <summary>The entry waiting for the user to confirm its deletion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDelete))]
    private PasswordEntry? _pendingDelete;

    /// <summary>True while a deletion waits for confirmation.</summary>
    public bool HasPendingDelete => PendingDelete is not null;

    [RelayCommand]
    private void RequestDelete(PasswordEntry? entry) => PendingDelete = entry;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void ConfirmDelete()
    {
        var entry = PendingDelete;
        PendingDelete = null;
        Delete(entry);
    }

    /// <summary>True when no entries match, so the view can show its empty state.</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Creates the view model and keeps <see cref="IsEmpty"/> in sync with the entries.</summary>
    public PasswordsViewModel()
    {
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Raised after an entry is added, updated or deleted so the vault can be saved.</summary>
    public event EventHandler? EntriesChanged;

    /// <summary>Raised when the view should copy text to the clipboard (and clear it later).</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>Replaces all entries and empty folders (used when the vault is unlocked).</summary>
    public void Load(
        IEnumerable<PasswordEntry> entries,
        IEnumerable<string>? folders = null,
        IReadOnlyDictionary<Guid, byte[]>? attachments = null,
        IReadOnlyDictionary<string, string>? folderIcons = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ClearBlobs();
        _all.Clear();
        _all.AddRange(entries.Select(WithCleanFolder));
        foreach (var attachment in _all.SelectMany(e => e.Attachments))
        {
            if (attachments is not null && attachments.TryGetValue(attachment.Id, out var bytes))
            {
                _blobs[attachment.Id] = bytes;
            }
        }

        _icons.Clear();
        foreach (var (path, key) in folderIcons ?? new Dictionary<string, string>())
        {
            var clean = FolderPath.Normalize(path);
            if (clean.Length > 0 && FolderIconCatalog.IsKnown(key))
            {
                _icons[clean] = key;
            }
        }

        _emptyFolders.Clear();
        foreach (var folder in (folders ?? []).Select(FolderPath.Normalize).Where(f => f.Length > 0))
        {
            _emptyFolders.Add(folder);
        }

        _selected.Clear();
        IsConfirmingBulkDelete = false;
        SelectedNode = null;
        ApplyFilter(null);
        ClearForm();
    }

    /// <summary>A vault file is untrusted, so a folder path in it is cleaned up before it is used.</summary>
    private static PasswordEntry WithCleanFolder(PasswordEntry entry)
    {
        var clean = FolderPath.Normalize(entry.Folder);
        return clean == entry.Folder ? entry : entry.WithFolder(clean);
    }

    /// <summary>
    /// Adds several entries (with their attachments, when supplied) and saves once. Returns how many attachments
    /// were left out because of the size limits.
    /// </summary>
    public int AddRange(IEnumerable<PasswordEntry> entries, IReadOnlyDictionary<Guid, byte[]>? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var before = _all.Count;
        var (fitted, dropped) = FitAttachments(entries.Select(WithCleanFolder), attachments);
        _all.AddRange(fitted);
        if (_all.Count > before)
        {
            ApplyFilter();
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }

        return dropped;
    }

    /// <summary>Returns a copy of all entries for persistence.</summary>
    public IReadOnlyList<PasswordEntry> Snapshot() => _all.ToList();

    partial void OnSearchTextChanged(string value)
    {
        // A tick on an entry that the new search hides could delete something the user can no longer see.
        _selected.Clear();
        IsConfirmingBulkDelete = false;
        ApplyFilter();
    }

    [RelayCommand]
    private void Generate() => Password = PasswordGenerator.Generate();

    [RelayCommand]
    private void Save()
    {
        var entry = new PasswordEntry
        {
            Id = _editingId ?? Guid.NewGuid(),
            Title = Title.Trim(),
            UserName = UserName.Trim(),
            Password = Password,
            Url = Url.Trim(),
            Notes = Notes,
            Tags = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Folder = FolderPath.Normalize(FormFolder?.Path),
            Attachments = FormAttachments.Select(r => r.Attachment).ToList(),
            ModifiedUtc = DateTimeOffset.UtcNow,
        };

        if (!entry.IsValid(out var error))
        {
            Status = error;
            return;
        }

        var index = _all.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            _all[index] = entry;
        }
        else
        {
            _all.Add(entry);
        }

        Status = $"Saved {entry.Title}.";
        ClearForm();
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Edit(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _editingId = entry.Id;
        Title = entry.Title;
        UserName = entry.UserName;
        Password = entry.Password;
        Url = entry.Url;
        Notes = entry.Notes;
        Tags = string.Join(", ", entry.Tags);
        FormFolder = ChoiceFor(entry.Folder) ?? FolderChoices.FirstOrDefault();
        FormAttachments.Clear();
        foreach (var attachment in entry.Attachments)
        {
            FormAttachments.Add(new AttachmentRow(attachment));
        }

        PruneBlobs();
        Status = $"Editing {entry.Title}.";
    }

    [RelayCommand]
    private void Delete(PasswordEntry? entry)
    {
        if (entry is null || _all.RemoveAll(e => e.Id == entry.Id) == 0)
        {
            return;
        }

        Status = $"Deleted {entry.Title}.";
        PruneBlobs();
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyPassword(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        CopyRequested?.Invoke(this, entry.Password);
        Status = $"Copied password for {entry.Title}. The clipboard clears in {ClipboardPolicy.ClearAfterText}.";
    }

    [RelayCommand]
    private void CopyUserName(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        CopyRequested?.Invoke(this, entry.UserName);
        Status = $"Copied user name for {entry.Title}.";
    }

    [RelayCommand]
    private void ClearForm()
    {
        _editingId = null;
        Title = UserName = Password = Url = Notes = Tags = string.Empty;
        FormFolder = ChoiceFor(SelectedNode?.Path ?? string.Empty) ?? FolderChoices.FirstOrDefault();
        FormAttachments.Clear();
        PruneBlobs();
    }

    /// <summary>Refreshes the folder list and the shown entries, keeping the selected folder.</summary>
    private void ApplyFilter() => ApplyFilter(SelectedNode?.Path);

    /// <summary>Refreshes the folder list and the shown entries, selecting <paramref name="folder"/> (null is "All passwords").</summary>
    private void ApplyFilter(string? folder)
    {
        RebuildFolders(folder);
        FilterEntries();
    }

    private void FilterEntries()
    {
        var q = SearchText.Trim();
        var folder = SelectedNode?.Path;
        Entries.Clear();
        Rows.Clear();
        _selected.IntersectWith(_all.Select(e => e.Id));
        foreach (var e in _all
            .Where(e => folder is null || FolderPath.IsInside(e.Folder, folder))
            .Where(e => q.Length == 0
                || e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Folder.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.UserName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Url.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
        {
            Entries.Add(e);
            Rows.Add(new PasswordRow(e, _selected.Contains(e.Id), SelectionChanged));
        }

        NotifySelectionChanged();
    }
}

/// <summary>One entry in the list with its check box.</summary>
public sealed partial class PasswordRow(PasswordEntry entry, bool isSelected, Action<PasswordRow> changed) : ObservableObject
{
    /// <summary>The entry.</summary>
    public PasswordEntry Entry { get; } = entry;

    /// <summary>True when files are attached to the entry.</summary>
    public bool HasAttachments => Entry.Attachments.Count > 0;

    /// <summary>For example "2 attached files".</summary>
    public string AttachmentsText => Entry.Attachments.Count == 1 ? "1 attached file" : $"{Entry.Attachments.Count} attached files";

    /// <summary>True while the entry is ticked.</summary>
    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => changed(this);
}
