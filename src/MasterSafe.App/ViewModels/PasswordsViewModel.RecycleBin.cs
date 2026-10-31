// File:    PasswordsViewModel.RecycleBin.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One earlier version of the entry that is being edited.</summary>
public sealed record HistoryRow(PasswordVersion Version)
{
    /// <summary>For example "2026-10-31 14:02: password changed".</summary>
    public string Text
    {
        get
        {
            var when = Version.ModifiedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return $"{when}: {Version.Title} ({Version.UserName})";
        }
    }
}

/// <summary>The recycle bin and the history of entries.</summary>
public sealed partial class PasswordsViewModel
{
    /// <summary>True while the recycle bin is the selected folder.</summary>
    public bool IsInBin => SelectedNode?.IsBin == true;

    /// <summary>True while the recycle bin is not the selected folder.</summary>
    public bool IsNotInBin => !IsInBin;

    /// <summary>How many entries are in the recycle bin.</summary>
    public int BinCount => _all.Count(e => e.IsDeleted);

    /// <summary>Entries that are in use (not in the recycle bin).</summary>
    private IEnumerable<PasswordEntry> Active => _all.Where(e => !e.IsDeleted);

    /// <summary>Returns a copy of the entries that are in use.</summary>
    public IReadOnlyList<PasswordEntry> ActiveSnapshot() => Active.ToList();

    /// <summary>The earlier versions of the entry in the form.</summary>
    public ObservableCollection<HistoryRow> FormHistory { get; } = [];

    /// <summary>True when the entry in the form has earlier versions.</summary>
    public bool HasHistory => FormHistory.Count > 0;

    /// <summary>True while emptying the recycle bin waits for confirmation.</summary>
    [ObservableProperty]
    private bool _isConfirmingEmptyBin;

    /// <summary>The question shown before one entry is deleted.</summary>
    public string DeleteQuestion => PendingDelete is null
        ? string.Empty
        : PendingDelete.IsDeleted
            ? $"Delete {PendingDelete.Title} for good? This cannot be undone."
            : $"Move {PendingDelete.Title} to the recycle bin? You can restore it for {PasswordVersion.BinRetentionDays} days.";

    /// <summary>The text of the button that confirms deleting one entry.</summary>
    public string DeleteConfirmText => PendingDelete?.IsDeleted == true ? "Yes, delete it for good" : "Yes, move it to the recycle bin";

    private void NotifyBinChanged()
    {
        OnPropertyChanged(nameof(IsInBin));
        OnPropertyChanged(nameof(IsNotInBin));
        OnPropertyChanged(nameof(BinCount));
        OnPropertyChanged(nameof(BulkDeleteQuestion));
        IsConfirmingEmptyBin = false;
    }

    partial void OnPendingDeleteChanged(PasswordEntry? value)
    {
        OnPropertyChanged(nameof(DeleteQuestion));
        OnPropertyChanged(nameof(DeleteConfirmText));
    }

    /// <summary>Takes an entry out of the recycle bin.</summary>
    [RelayCommand]
    private void Restore(PasswordEntry? entry)
    {
        var index = entry is null ? -1 : _all.FindIndex(e => e.Id == entry.Id && e.IsDeleted);
        if (index < 0)
        {
            return;
        }

        var restored = _all[index].WithDeleted(null);
        _all[index] = restored;
        Status = $"Restored {restored.Title}{(restored.Folder.Length > 0 ? $" to {restored.Folder}" : string.Empty)}.";
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RequestEmptyBin()
    {
        if (BinCount > 0)
        {
            IsConfirmingEmptyBin = true;
        }
    }

    [RelayCommand]
    private void CancelEmptyBin() => IsConfirmingEmptyBin = false;

    /// <summary>Deletes everything in the recycle bin for good.</summary>
    [RelayCommand]
    private void ConfirmEmptyBin()
    {
        IsConfirmingEmptyBin = false;
        var removed = _all.RemoveAll(e => e.IsDeleted);
        if (removed == 0)
        {
            return;
        }

        _selected.Clear();
        PruneBlobs();
        Status = $"Deleted {removed} {(removed == 1 ? "password" : "passwords")} for good.";
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes entries that have been in the recycle bin longer than the retention time.</summary>
    private void PurgeOldBinEntries()
    {
        var limit = DateTimeOffset.UtcNow - TimeSpan.FromDays(PasswordVersion.BinRetentionDays);
        _all.RemoveAll(e => e.DeletedUtc is { } deleted && deleted < limit);
    }

    /// <summary>Works out the history of an entry that is being saved: the old values are added when something changed.</summary>
    private static IReadOnlyList<PasswordVersion> BuildHistory(PasswordEntry? existing, PasswordEntry updated)
    {
        if (existing is null)
        {
            return [];
        }

        var changed = existing.Title != updated.Title
            || existing.UserName != updated.UserName
            || existing.Password != updated.Password
            || existing.Url != updated.Url
            || existing.Notes != updated.Notes;

        return changed
            ? [existing.ToVersion(), .. existing.History.Take(PasswordVersion.MaxKept - 1)]
            : existing.History;
    }

    private void ShowHistory(PasswordEntry entry)
    {
        FormHistory.Clear();
        foreach (var version in entry.History)
        {
            FormHistory.Add(new HistoryRow(version));
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>Puts an earlier version back into the form. It is only kept when the user saves.</summary>
    [RelayCommand]
    private void UseVersion(HistoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        Title = row.Version.Title;
        UserName = row.Version.UserName;
        Password = row.Version.Password;
        Url = row.Version.Url;
        Notes = row.Version.Notes;
        Status = $"Loaded the version from {row.Version.ModifiedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}. Choose Save to keep it.";
    }

    /// <summary>Copies the password of an earlier version, for example to sign in to a site that still has the old one.</summary>
    [RelayCommand]
    private void CopyVersionPassword(HistoryRow? row)
    {
        if (row is null)
        {
            return;
        }

        CopyRequested?.Invoke(this, row.Version.Password);
        Status = $"Copied the old password. The clipboard clears in {Services.ClipboardPolicy.ClearAfterText}.";
    }
}
