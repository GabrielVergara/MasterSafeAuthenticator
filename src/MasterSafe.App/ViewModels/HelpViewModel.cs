// File:    HelpViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterSafe.App.Help;
using MasterSafe.App.Services;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Help page: lists the built-in help topics, lets the user search them, and shows the selected article.
/// </summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    [ObservableProperty]
    private HelpTopic? _selectedTopic;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public HelpViewModel()
    {
        Topics = new ObservableCollection<HelpTopic>(Loc.LocalizeHelp(HelpContent.Topics));
        _selectedTopic = Topics.Count > 0 ? Topics[0] : null;
    }

    /// <summary>The help topics that match the search (all of them when it is empty).</summary>
    public ObservableCollection<HelpTopic> Topics { get; }

    /// <summary>True when the search matches no topic.</summary>
    public bool HasNoResults => Topics.Count == 0;

    partial void OnSearchTextChanged(string value)
    {
        var q = value.Trim();
        var matches = Loc.LocalizeHelp(HelpContent.Topics)
            .Where(t => q.Length == 0
                || t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.Body.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Topics.Clear();
        foreach (var topic in matches)
        {
            Topics.Add(topic);
        }

        if (SelectedTopic is null || !Topics.Contains(SelectedTopic))
        {
            SelectedTopic = Topics.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasNoResults));
    }
}
