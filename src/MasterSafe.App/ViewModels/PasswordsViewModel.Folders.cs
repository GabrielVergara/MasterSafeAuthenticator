// File:    PasswordsViewModel.Folders.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One line of the folder list. <see cref="Path"/> is null for "All passwords".</summary>
public sealed record FolderNode(string? Path, string Name, int Depth, int Count)
{
    /// <summary>Text shown in the list.</summary>
    public string Display => $"{Name} ({Count})";

    /// <summary>Indent that shows how deep the folder is.</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);
}

/// <summary>A folder that can be picked for an entry. An empty <see cref="Path"/> is the top level.</summary>
public sealed record FolderChoice(string Path, string Display);

/// <summary>Folders of the password manager. Passwords only; authenticator accounts are not part of this.</summary>
public sealed partial class PasswordsViewModel
{
    // Folders that have no entries yet. A folder with entries exists because of them.
    private readonly HashSet<string> _emptyFolders = new(StringComparer.OrdinalIgnoreCase);
    private bool _rebuildingFolders;

    /// <summary>The folder list, starting with "All passwords".</summary>
    public ObservableCollection<FolderNode> FolderNodes { get; } = [];

    /// <summary>The folders that can be chosen for an entry, starting with "(No folder)".</summary>
    public ObservableCollection<FolderChoice> FolderChoices { get; } = [];

    /// <summary>The folder whose passwords are shown (null node path means all of them).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderSelected))]
    private FolderNode? _selectedNode;

    /// <summary>The folder chosen in the entry form.</summary>
    [ObservableProperty]
    private FolderChoice? _formFolder;

    /// <summary>Where ticked entries are moved to.</summary>
    [ObservableProperty]
    private FolderChoice? _moveTarget;

    /// <summary>Name typed for a new folder, or the new name when renaming.</summary>
    [ObservableProperty]
    private string _folderName = string.Empty;

    /// <summary>True when a real folder (not "All passwords") is selected.</summary>
    public bool HasFolderSelected => SelectedNode?.Path is not null;

    /// <summary>The empty folders, for saving in the vault.</summary>
    public IReadOnlyList<string> FolderSnapshot() => _emptyFolders.Order(StringComparer.OrdinalIgnoreCase).ToList();

    partial void OnSelectedNodeChanged(FolderNode? value)
    {
        if (_rebuildingFolders)
        {
            return;
        }

        // Ticks on entries that this folder does not show could delete something out of sight.
        _selected.Clear();
        IsConfirmingBulkDelete = false;
        if (_editingId is null)
        {
            FormFolder = ChoiceFor(value?.Path ?? string.Empty);
        }

        FilterEntries();
    }

    [RelayCommand]
    private void CreateFolder()
    {
        var name = FolderPath.Normalize(FolderName);
        if (name.Length == 0)
        {
            Status = "Type a name for the folder first.";
            return;
        }

        var path = FolderPath.Combine(SelectedNode?.Path ?? string.Empty, name);
        if (FolderPath.Depth(path) > FolderPath.MaxDepth || FolderExists(path))
        {
            Status = FolderExists(path) ? "That folder already exists." : "Folders cannot be nested that deeply.";
            return;
        }

        _emptyFolders.Add(path);
        FolderName = string.Empty;
        Status = $"Created folder {path}.";
        ApplyFilter(path);
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RenameFolder()
    {
        var old = SelectedNode?.Path;
        var name = FolderPath.Normalize(FolderName);
        if (old is null)
        {
            Status = "Choose the folder to rename first.";
            return;
        }

        if (name.Length == 0)
        {
            Status = "Type the new name for the folder first.";
            return;
        }

        var renamed = FolderPath.Combine(FolderPath.Parent(old), name);
        if (renamed == old)
        {
            return;
        }

        if (!renamed.Equals(old, StringComparison.OrdinalIgnoreCase) && FolderExists(renamed))
        {
            Status = "A folder with that name already exists.";
            return;
        }

        Relocate(old, renamed);
        FolderName = string.Empty;
        Status = $"Renamed the folder to {renamed}.";
        ApplyFilter(renamed);
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes a folder. Its passwords are not deleted: they move up to the folder above.</summary>
    [RelayCommand]
    private void DeleteFolder()
    {
        var old = SelectedNode?.Path;
        if (old is null)
        {
            return;
        }

        var parent = FolderPath.Parent(old);
        var moved = _all.Count(e => FolderPath.IsInside(e.Folder, old));
        Relocate(old, parent, keepFolder: false);
        Status = moved == 0
            ? $"Deleted folder {old}."
            : $"Deleted folder {old}. Its {moved} {(moved == 1 ? "password was" : "passwords were")} moved to {(parent.Length == 0 ? "the top level" : parent)}.";
        ApplyFilter(parent.Length == 0 ? null : parent);
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the ticked entries into the chosen folder.</summary>
    [RelayCommand]
    private void MoveSelected()
    {
        if (!HasSelection || MoveTarget is null)
        {
            return;
        }

        var target = MoveTarget.Path;
        var ids = _selected.ToHashSet();
        var moved = 0;
        for (var i = 0; i < _all.Count; i++)
        {
            if (ids.Contains(_all[i].Id) && !_all[i].Folder.Equals(target, StringComparison.Ordinal))
            {
                _all[i] = _all[i].WithFolder(target);
                moved++;
            }
        }

        _selected.Clear();
        IsConfirmingBulkDelete = false;
        Status = $"Moved {moved} {(moved == 1 ? "password" : "passwords")} to {(target.Length == 0 ? "the top level" : target)}.";
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool FolderExists(string path)
        => _emptyFolders.Contains(path) || _all.Any(e => FolderPath.IsInside(e.Folder, path));

    private FolderChoice? ChoiceFor(string path)
        => FolderChoices.FirstOrDefault(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Renames <paramref name="from"/> (and everything below it) to <paramref name="to"/>.</summary>
    private void Relocate(string from, string to, bool keepFolder = true)
    {
        string Map(string path) => path.Length == from.Length ? to : FolderPath.Normalize(to + FolderPath.Separator + path[(from.Length + 1)..]);

        for (var i = 0; i < _all.Count; i++)
        {
            if (FolderPath.IsInside(_all[i].Folder, from))
            {
                _all[i] = _all[i].WithFolder(Map(_all[i].Folder));
            }
        }

        var affected = _emptyFolders.Where(f => FolderPath.IsInside(f, from)).ToList();
        foreach (var folder in affected)
        {
            _emptyFolders.Remove(folder);
            var mapped = Map(folder);
            if (keepFolder || !folder.Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                if (mapped.Length > 0)
                {
                    _emptyFolders.Add(mapped);
                }
            }
        }
    }

    /// <summary>Rebuilds the folder list and the folder choices, and keeps the current selections where they still exist.</summary>
    private void RebuildFolders(string? selectPath)
    {
        _rebuildingFolders = true;
        try
        {
            var paths = _all.Select(e => e.Folder)
                .Concat(_emptyFolders)
                .Where(p => p.Length > 0)
                .SelectMany(FolderPath.WithAncestors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p.Replace(FolderPath.Separator, '\u0001'), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var formPath = FormFolder?.Path ?? string.Empty;
            var movePath = MoveTarget?.Path;

            FolderNodes.Clear();
            FolderNodes.Add(new FolderNode(null, "All passwords", 0, _all.Count));
            FolderChoices.Clear();
            FolderChoices.Add(new FolderChoice(string.Empty, "(No folder)"));
            foreach (var path in paths)
            {
                var depth = FolderPath.Depth(path) - 1;
                FolderNodes.Add(new FolderNode(path, FolderPath.Name(path), depth, _all.Count(e => FolderPath.IsInside(e.Folder, path))));
                FolderChoices.Add(new FolderChoice(path, new string(' ', depth * 2) + FolderPath.Name(path)));
            }

            SelectedNode = FolderNodes.FirstOrDefault(n => n.Path is not null && selectPath is not null && n.Path.Equals(selectPath, StringComparison.OrdinalIgnoreCase))
                ?? FolderNodes[0];
            FormFolder = ChoiceFor(formPath) ?? FolderChoices[0];
            MoveTarget = movePath is null ? null : ChoiceFor(movePath);
        }
        finally
        {
            _rebuildingFolders = false;
        }
    }
}
