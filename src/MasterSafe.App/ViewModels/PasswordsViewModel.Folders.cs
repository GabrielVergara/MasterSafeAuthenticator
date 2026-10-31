// File:    PasswordsViewModel.Folders.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One line of the folder list. <see cref="Path"/> is null for "All passwords".</summary>
public sealed record FolderNode(string? Path, string Name, int Depth, int Count, string IconKey)
{
    /// <summary>Text shown in the list.</summary>
    public string Display => $"{Name} ({Count})";

    /// <summary>The icon drawn in front of the name.</summary>
    public Geometry Icon => FolderIconCatalog.GeometryFor(IconKey);

    /// <summary>Indent that shows how deep the folder is.</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);
}

/// <summary>A folder that can be picked for an entry. An empty <see cref="Path"/> is the top level.</summary>
public sealed record FolderChoice(string Path, string Display, string IconKey)
{
    /// <summary>The icon drawn in front of the name.</summary>
    public Geometry Icon => FolderIconCatalog.GeometryFor(IconKey);
}

/// <summary>Folders of the password manager. Passwords only; authenticator accounts are not part of this.</summary>
public sealed partial class PasswordsViewModel
{
    // Folders that have no entries yet. A folder with entries exists because of them.
    private readonly HashSet<string> _emptyFolders = new(StringComparer.OrdinalIgnoreCase);
    private bool _rebuildingFolders;

    // Folder path to the key of its icon. Folders that are not in here use the default icon.
    private readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icons a folder can be given.</summary>
    public IReadOnlyList<FolderIcon> IconChoices => FolderIconCatalog.All;

    /// <summary>The chosen icons, for saving in the vault. Folders that no longer exist are left out.</summary>
    public IReadOnlyDictionary<string, string> IconSnapshot()
    {
        var existing = _all.Select(e => e.Folder).Concat(_emptyFolders)
            .Where(p => p.Length > 0).SelectMany(FolderPath.WithAncestors)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _icons.Where(i => existing.Contains(i.Key)).ToDictionary(i => i.Key, i => i.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gives the selected folder an icon.</summary>
    [RelayCommand]
    private void SetFolderIcon(FolderIcon? icon)
    {
        var path = SelectedNode?.Path;
        if (path is null || icon is null)
        {
            Status = "Choose a folder first, then pick its icon.";
            return;
        }

        if (icon.Key == FolderIconCatalog.DefaultKey)
        {
            _icons.Remove(path);
        }
        else
        {
            _icons[path] = icon.Key;
        }

        Status = $"Changed the icon of {path}.";
        ApplyFilter(path);
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private string IconKeyFor(string path) => _icons.TryGetValue(path, out var key) ? key : FolderIconCatalog.DefaultKey;

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

    /// <summary>Narrowest and widest the folder list is made automatically.</summary>
    public const double MinAutoPaneWidth = 170;

    /// <summary>Widest the folder list is made automatically.</summary>
    public const double MaxAutoPaneWidth = 340;

    /// <summary>Raised when the user drags the divider or resets the width, so the choice can be remembered.</summary>
    public event EventHandler? FolderPaneWidthChanged;

    private double? _userFolderPaneWidth;

    /// <summary>The width the user chose for the folder list, or null to fit it to the folder names.</summary>
    public double? UserFolderPaneWidth
    {
        get => _userFolderPaneWidth;
        set
        {
            var clean = value is { } v && v >= 100 && v <= 800 ? v : (double?)null;
            if (SetProperty(ref _userFolderPaneWidth, clean))
            {
                OnPropertyChanged(nameof(EffectiveFolderPaneWidth));
                FolderPaneWidthChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The width that fits the longest folder line, so names are not cut off.</summary>
    public double SuggestedFolderPaneWidth
    {
        get
        {
            // About 7.4 pixels per character, plus the icon, the indent and the padding around the list.
            var widest = FolderNodes.Select(n => n.Depth * 14 + 26 + n.Display.Length * 7.4).DefaultIfEmpty(0).Max();
            return Math.Clamp(Math.Ceiling(widest + 44), MinAutoPaneWidth, MaxAutoPaneWidth);
        }
    }

    /// <summary>The width the folder list should have now: the user's choice, or the one that fits.</summary>
    public double EffectiveFolderPaneWidth => UserFolderPaneWidth ?? SuggestedFolderPaneWidth;

    /// <summary>Loads the remembered width without announcing a change.</summary>
    public void RestoreFolderPaneWidth(double width)
    {
        _userFolderPaneWidth = width >= 100 && width <= 800 ? width : null;
        OnPropertyChanged(nameof(UserFolderPaneWidth));
        OnPropertyChanged(nameof(EffectiveFolderPaneWidth));
    }

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

        foreach (var (path, key) in _icons.Where(i => FolderPath.IsInside(i.Key, from)).ToList())
        {
            _icons.Remove(path);
            if (keepFolder || !path.Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                var mapped = Map(path);
                if (mapped.Length > 0)
                {
                    _icons[mapped] = key;
                }
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
            FolderNodes.Add(new FolderNode(null, "All passwords", 0, _all.Count, "key"));
            FolderChoices.Clear();
            FolderChoices.Add(new FolderChoice(string.Empty, "(No folder)", "lock"));
            foreach (var path in paths)
            {
                var depth = FolderPath.Depth(path) - 1;
                var icon = IconKeyFor(path);
                FolderNodes.Add(new FolderNode(path, FolderPath.Name(path), depth, _all.Count(e => FolderPath.IsInside(e.Folder, path)), icon));
                FolderChoices.Add(new FolderChoice(path, new string(' ', depth * 2) + FolderPath.Name(path), icon));
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
