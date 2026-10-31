// File:    PasswordsViewModel.Usability.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One way to order the list of passwords.</summary>
public sealed record PasswordSortOption(string Key, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>Sorting, the current entry, keyboard shortcuts, opening a site and dragging entries to folders.</summary>
public sealed partial class PasswordsViewModel
{
    /// <summary>The orders the list can be shown in.</summary>
    public static IReadOnlyList<PasswordSortOption> SortOptions { get; } =
    [
        new("title", "Title, A to Z"),
        new("title-desc", "Title, Z to A"),
        new("changed", "Recently changed"),
        new("folder", "Folder, then title"),
    ];

    private Guid? _currentId;

    /// <summary>How the list is ordered.</summary>
    [ObservableProperty]
    private PasswordSortOption _selectedSort = SortOptions[0];

    /// <summary>Raised when the user picks another order, so it can be remembered.</summary>
    public event EventHandler? SortChanged;

    /// <summary>Raised when the entry form should take the keyboard focus (new entry).</summary>
    public event EventHandler? FocusTitleRequested;

    /// <summary>Raised when the search box should take the keyboard focus.</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Opens a web address. Replaced in tests; by default it uses the system browser.</summary>
    public Action<string> UrlOpener { get; set; } = OpenInBrowser;

    private bool _restoringSort;

    partial void OnSelectedSortChanged(PasswordSortOption value)
    {
        FilterEntries();
        if (!_restoringSort)
        {
            SortChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Uses a remembered order without announcing a change. Unknown keys give the default.</summary>
    public void RestoreSort(string? key)
    {
        _restoringSort = true;
        SelectedSort = SortOptions.FirstOrDefault(o => o.Key == key) ?? SortOptions[0];
        _restoringSort = false;
    }

    private IEnumerable<PasswordEntry> Sorted(IEnumerable<PasswordEntry> source, bool inBin)
    {
        if (inBin)
        {
            return source.OrderByDescending(e => e.DeletedUtc ?? DateTimeOffset.MinValue).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase);
        }

        return SelectedSort.Key switch
        {
            "title-desc" => source.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),
            "changed" => source.OrderByDescending(e => e.ModifiedUtc).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
            "folder" => source.OrderBy(e => e.Folder.Replace(FolderPath.Separator, '\u0001'), StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
        };
    }

    // ------------------------------------------------------------------ the current entry

    /// <summary>The entry that shortcuts act on: the one last clicked or edited, or the only one in the list.</summary>
    public PasswordEntry? CurrentEntry
    {
        get
        {
            var current = _currentId is { } id ? Rows.FirstOrDefault(r => r.Entry.Id == id)?.Entry : null;
            return current ?? (Rows.Count == 1 ? Rows[0].Entry : null);
        }
    }

    /// <summary>Marks an entry as the current one.</summary>
    [RelayCommand]
    private void SetCurrent(PasswordEntry? entry)
    {
        _currentId = entry?.Id;
        foreach (var row in Rows)
        {
            row.IsCurrent = row.Entry.Id == _currentId;
        }
    }

    /// <summary>Copies the user name of the current entry.</summary>
    [RelayCommand]
    private void CopyCurrentUser()
    {
        if (CurrentEntry is { IsDeleted: false } entry)
        {
            CopyUserName(entry);
        }
    }

    /// <summary>Copies the password of the current entry.</summary>
    [RelayCommand]
    private void CopyCurrentPassword()
    {
        if (CurrentEntry is { IsDeleted: false } entry)
        {
            CopyPassword(entry);
        }
    }

    /// <summary>Opens the web address of the current entry.</summary>
    [RelayCommand]
    private void OpenCurrentUrl()
    {
        if (CurrentEntry is { IsDeleted: false } entry)
        {
            OpenUrl(entry);
        }
    }

    /// <summary>Asks to delete the current entry (it goes to the recycle bin after a confirmation).</summary>
    [RelayCommand]
    private void DeleteCurrent()
    {
        if (CurrentEntry is { } entry)
        {
            RequestDelete(entry);
        }
    }

    /// <summary>Starts a new entry in the form and moves the focus there.</summary>
    [RelayCommand]
    private void NewEntry()
    {
        ClearForm();
        FocusTitleRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the focus to the search box.</summary>
    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------ opening a site

    /// <summary>
    /// Turns what is typed in the address box into an address that is safe to open: only http and https, with a host.
    /// An address without a scheme gets https. Everything else (file:, javascript:, a program name...) is refused.
    /// </summary>
    public static bool TryNormalizeUrl(string? text, out string url)
    {
        url = string.Empty;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > 2048 || trimmed.Any(char.IsControl) || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        // "example.com/login" has no scheme; "javascript:alert(1)" and "file:///c:/x" have one that is not allowed.
        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    /// <summary>Opens the web address of an entry in the browser.</summary>
    [RelayCommand]
    private void OpenUrl(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!TryNormalizeUrl(entry.Url, out var url))
        {
            Status = string.IsNullOrWhiteSpace(entry.Url)
                ? $"{entry.Title} has no web address."
                : $"The address of {entry.Title} is not a web address MasterSafe will open (only http and https).";
            return;
        }

        UrlOpener(url);
        Status = url.StartsWith("http://", StringComparison.Ordinal)
            ? $"Opened {url}. This address is not encrypted (http): do not enter your password there."
            : $"Opened {url}.";
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser is available; nothing else to do.
        }
    }

    // ------------------------------------------------------------------ dragging entries to folders

    /// <summary>
    /// Moves an entry that was dragged onto a folder. When the dragged entry is ticked, all ticked entries move with it.
    /// Dropping on the recycle bin deletes (restorably). Returns the number of entries that moved.
    /// </summary>
    public int MoveDraggedTo(Guid draggedId, FolderNode? target)
    {
        if (target is null)
        {
            return 0;
        }

        if (target is { IsBin: false, Path: null })
        {
            Status = "Drop the password on a folder to move it there. Use the Folder box in the form for the top level.";
            return 0;
        }

        var ids = _selected.Contains(draggedId) ? _selected.ToHashSet() : [draggedId];
        var moved = 0;
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < _all.Count; i++)
        {
            var entry = _all[i];
            if (!ids.Contains(entry.Id))
            {
                continue;
            }

            if (target.IsBin)
            {
                if (!entry.IsDeleted)
                {
                    _all[i] = entry.WithDeleted(now);
                    moved++;
                }
            }
            else if (!entry.IsDeleted && !entry.Folder.Equals(target.Path, StringComparison.Ordinal))
            {
                _all[i] = entry.WithFolder(target.Path!);
                moved++;
            }
        }

        if (moved == 0)
        {
            return 0;
        }

        _selected.Clear();
        var noun = moved == 1 ? "password" : "passwords";
        Status = target.IsBin ? $"Moved {moved} {noun} to the recycle bin." : $"Moved {moved} {noun} to {target.Path}.";
        PruneBlobs();
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        return moved;
    }
}
