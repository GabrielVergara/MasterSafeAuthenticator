// File:    DashboardViewModel.Extras.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One way to order the authenticator accounts.</summary>
public sealed record AccountSortOption(string Key, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>A reminder shown at the top of the codes page, for example "You have no recovery key yet".</summary>
/// <param name="Key">Identifies the reminder, so that one that was dismissed stays away for the session.</param>
/// <param name="Text">What to tell the user.</param>
/// <param name="ActionText">Label of the button that deals with it.</param>
/// <param name="Action">What the button does.</param>
public sealed record NoticeRow(string Key, string Text, string ActionText, ICommand Action);

/// <summary>Search, sorting, renaming and reminders on the codes page.</summary>
public sealed partial class DashboardViewModel
{
    private readonly HashSet<string> _dismissedNotices = [];

    /// <summary>The orders the accounts can be shown in.</summary>
    public static IReadOnlyList<AccountSortOption> SortOptions { get; } =
    [
        new("added", "My own order"),
        new("name", "Name, A to Z"),
        new("name-desc", "Name, Z to A"),
    ];

    /// <summary>The accounts that match the search, in the chosen order. This is what the page lists.</summary>
    public BulkObservableCollection<OtpItemViewModel> Shown { get; } = [];

    /// <summary>How the accounts are ordered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReorder))]
    private AccountSortOption _selectedSort = SortOptions[0];

    private readonly Dictionary<Guid, DateTimeOffset> _tombstones = [];

    /// <summary>Accounts deleted for good, for sync.</summary>
    public IReadOnlyList<MasterSafe.Crypto.Tombstone> TombstoneSnapshot()
        => _tombstones.Select(t => new MasterSafe.Crypto.Tombstone { Id = t.Key, DeletedUtc = t.Value }).ToList();

    /// <summary>Codes stay covered until they are clicked.</summary>
    [ObservableProperty]
    private bool _hideCodes;

    partial void OnHideCodesChanged(bool value)
    {
        foreach (var item in Accounts)
        {
            item.CanHide = value;
            item.IsHidden = value;
        }
    }

    private OtpItemViewModel NewItem(OtpAccount account) => new(account) { CanHide = HideCodes, IsHidden = HideCodes };

    /// <summary>Shows a covered code for a short time, or covers it again.</summary>
    [RelayCommand]
    private void ToggleReveal(OtpItemViewModel? item) => item?.ToggleReveal();

    /// <summary>The groups in use, for the filter. The first one means all.</summary>
    public ObservableCollection<string> GroupChoices { get; } = [];

    /// <summary>The group shown. The first choice shows all accounts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReorder))]
    private string? _selectedGroup;

    /// <summary>True when groups are in use, so the filter is worth showing.</summary>
    public bool HasGroups => GroupChoices.Count > 1;

    private bool _rebuildingGroups;

    private void RebuildGroups()
    {
        var wanted = SelectedGroup;
        var groups = Accounts.Select(a => a.Group).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        _rebuildingGroups = true;
        GroupChoices.Clear();
        GroupChoices.Add(Loc.T("All groups"));
        foreach (var group in groups)
        {
            GroupChoices.Add(group);
        }

        SelectedGroup = wanted is not null && GroupChoices.Contains(wanted) ? wanted : GroupChoices[0];
        _rebuildingGroups = false;
        OnPropertyChanged(nameof(HasGroups));
        RebuildShown();
    }

    partial void OnSelectedGroupChanged(string? value)
    {
        if (!_rebuildingGroups)
        {
            RebuildShown();
        }
    }

    private bool ShowsAllGroups => SelectedGroup is null || GroupChoices.Count == 0 || SelectedGroup == GroupChoices[0];

    /// <summary>True when the arrows that move an account are offered: only your own order, with nothing filtered out.</summary>
    public bool CanReorder => SelectedSort.Key == "added" && SearchText.Trim().Length == 0 && ShowsAllGroups;

    /// <summary>Moves an account up in your own order.</summary>
    [RelayCommand]
    private void MoveUp(OtpItemViewModel? item) => Move(item, -1);

    /// <summary>Moves an account down in your own order.</summary>
    [RelayCommand]
    private void MoveDown(OtpItemViewModel? item) => Move(item, 1);

    private void Move(OtpItemViewModel? item, int step)
    {
        var from = item is null ? -1 : Accounts.IndexOf(item);
        var to = from + step;
        if (!CanReorder || from < 0 || to < 0 || to >= Accounts.Count)
        {
            return;
        }

        Accounts.Move(from, to);
        StatusMessage = $"Moved {item!.Issuer}.";
        AccountsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user picks another order, so it can be remembered.</summary>
    public event EventHandler? SortChanged;

    private bool _restoringSort;

    /// <summary>True when there are accounts but none matches the search.</summary>
    public bool HasNoMatches => Accounts.Count > 0 && Shown.Count == 0;

    /// <summary>True when there are enough accounts for search and sorting to be useful.</summary>
    public bool ShowSearch => Accounts.Count > 3;

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanReorder));
        RebuildShown();
    }

    partial void OnSelectedSortChanged(AccountSortOption value)
    {
        RebuildShown();
        if (!_restoringSort)
        {
            SortChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Uses a remembered order without announcing a change.</summary>
    public void RestoreSort(string? key)
    {
        _restoringSort = true;
        SelectedSort = SortOptions.FirstOrDefault(o => o.Key == key) ?? SortOptions[0];
        _restoringSort = false;
    }

    private void RebuildShown()
    {
        var q = SearchText.Trim();
        IEnumerable<OtpItemViewModel> list = Accounts
            .Where(a => ShowsAllGroups || string.Equals(a.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase))
            .Where(a => q.Length == 0
                || a.Issuer.Contains(q, StringComparison.OrdinalIgnoreCase)
                || a.Label.Contains(q, StringComparison.OrdinalIgnoreCase));

        list = SelectedSort.Key switch
        {
            "name" => list.OrderBy(a => a.Issuer, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase),
            "name-desc" => list.OrderByDescending(a => a.Issuer, StringComparer.OrdinalIgnoreCase).ThenByDescending(a => a.Label, StringComparer.OrdinalIgnoreCase),
            _ => list,
        };

        var wanted = list.ToList();
        if (wanted.SequenceEqual(Shown))
        {
            return;
        }

        Shown.ReplaceAll(wanted);
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(ShowSearch));
    }

    // ------------------------------------------------------------------ renaming

    /// <summary>The account that is being renamed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private OtpItemViewModel? _editing;

    /// <summary>The issuer being typed.</summary>
    [ObservableProperty]
    private string _editIssuer = string.Empty;

    /// <summary>The label being typed.</summary>
    [ObservableProperty]
    private string _editLabel = string.Empty;

    /// <summary>The group being typed.</summary>
    [ObservableProperty]
    private string _editGroup = string.Empty;

    /// <summary>True while an account is being renamed.</summary>
    public bool IsEditing => Editing is not null;

    /// <summary>Starts renaming an account (its issuer, such as "GitHub", and its label, such as the user name).</summary>
    [RelayCommand]
    private void BeginEdit(OtpItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        PendingDelete = null;
        Editing = item;
        EditIssuer = item.Account.Issuer;
        EditLabel = item.Account.Label;
        EditGroup = item.Account.Group;
    }

    [RelayCommand]
    private void CancelEdit() => Editing = null;

    /// <summary>Saves the new names. The secret, type and settings of the account are not touched.</summary>
    [RelayCommand]
    private void SaveEdit()
    {
        var item = Editing;
        if (item is null)
        {
            return;
        }

        var issuer = EditIssuer.Trim();
        var label = EditLabel.Trim();
        if (issuer.Length == 0 && label.Length == 0)
        {
            StatusMessage = "Type an issuer or a label.";
            return;
        }

        var index = Accounts.IndexOf(item);
        if (index < 0)
        {
            Editing = null;
            return;
        }

        var renamed = item.Account.Renamed(issuer, label, EditGroup.Trim(), _timeProvider());
        Accounts[index] = NewItem(renamed);
        Editing = null;
        RebuildGroups();
        Refresh();
        StatusMessage = $"Renamed to {(issuer.Length > 0 ? issuer : label)}.";
        AccountsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ reminders

    /// <summary>Reminders at the top of the page.</summary>
    public ObservableCollection<NoticeRow> Notices { get; } = [];

    /// <summary>Shows these reminders, leaving out the ones that were dismissed in this session.</summary>
    public void SetNotices(IEnumerable<NoticeRow> notices)
    {
        Notices.Clear();
        foreach (var notice in notices.Where(n => !_dismissedNotices.Contains(n.Key)))
        {
            Notices.Add(notice);
        }
    }

    /// <summary>Hides a reminder until the app is started again.</summary>
    [RelayCommand]
    private void DismissNotice(NoticeRow? notice)
    {
        if (notice is null)
        {
            return;
        }

        _dismissedNotices.Add(notice.Key);
        Notices.Remove(notice);
    }

    /// <summary>Forgets what was dismissed (used when the vault is locked).</summary>
    public void ResetNotices()
    {
        _dismissedNotices.Clear();
        Notices.Clear();
    }
}
