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
        ClearForm();
        ApplyFilter();
    }

    /// <summary>Returns a copy of all entries for persistence.</summary>
    public IReadOnlyList<PasswordEntry> Snapshot() => _all.ToList();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

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
        foreach (var e in _all
            .Where(e => q.Length == 0
                || e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.UserName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Url.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
        {
            Entries.Add(e);
        }
    }
}
