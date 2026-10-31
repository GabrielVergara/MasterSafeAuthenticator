using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SafeAuthenticator.App.Help;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// Help page: lists the built-in help topics and shows the selected article.
/// </summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    [ObservableProperty]
    private HelpTopic? _selectedTopic;

    public HelpViewModel()
    {
        Topics = new ObservableCollection<HelpTopic>(HelpContent.Topics);
        _selectedTopic = Topics.Count > 0 ? Topics[0] : null;
    }

    /// <summary>All available help topics.</summary>
    public ObservableCollection<HelpTopic> Topics { get; }
}
