// File:    PasswordsViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Replaces all entries (used when the vault is unlocked).</summary>
    public void Load(IEnumerable<PasswordEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _all.Clear();
        _all.AddRange(entries);
        _selected.Clear();
        IsConfirmingBulkDelete = false;
        ClearForm();
        ApplyFilter();
    }

    /// <summary>Adds several entries and saves once.</summary>
    public void AddRange(IEnumerable<PasswordEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var before = _all.Count;
        _all.AddRange(entries);
        if (_all.Count > before)
        {
            ApplyFilter();
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
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
        Status = $"Copied password for {entry.Title}. The clipboard clears in 30 seconds.";
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
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        Entries.Clear();
        Rows.Clear();
        _selected.IntersectWith(_all.Select(e => e.Id));
        foreach (var e in _all
            .Where(e => q.Length == 0
                || e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
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

    /// <summary>True while the entry is ticked.</summary>
    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => changed(this);
}
