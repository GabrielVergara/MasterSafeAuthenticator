// File:    DashboardViewModel.Extras.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        new("added", "Order added"),
        new("name", "Name, A to Z"),
        new("name-desc", "Name, Z to A"),
    ];

    /// <summary>The accounts that match the search, in the chosen order. This is what the page lists.</summary>
    public ObservableCollection<OtpItemViewModel> Shown { get; } = [];

    /// <summary>How the accounts are ordered.</summary>
    [ObservableProperty]
    private AccountSortOption _selectedSort = SortOptions[0];

    /// <summary>Raised when the user picks another order, so it can be remembered.</summary>
    public event EventHandler? SortChanged;

    private bool _restoringSort;

    /// <summary>True when there are accounts but none matches the search.</summary>
    public bool HasNoMatches => Accounts.Count > 0 && Shown.Count == 0;

    /// <summary>True when there are enough accounts for search and sorting to be useful.</summary>
    public bool ShowSearch => Accounts.Count > 3;

    partial void OnSearchTextChanged(string value) => RebuildShown();

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
        IEnumerable<OtpItemViewModel> list = Accounts.Where(a => q.Length == 0
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

        Shown.Clear();
        foreach (var item in wanted)
        {
            Shown.Add(item);
        }

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

        var old = item.Account;
        var renamed = new OtpAccount
        {
            Id = old.Id,
            Issuer = issuer,
            Label = label,
            Secret = old.Secret,
            Type = old.Type,
            Algorithm = old.Algorithm,
            Digits = old.Digits,
            Period = old.Period,
            Counter = old.Counter,
            Tags = old.Tags,
        };

        Accounts[index] = new OtpItemViewModel(renamed);
        Editing = null;
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
