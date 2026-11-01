// File:    GuideViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;

namespace MasterSafe.App.ViewModels;

/// <summary>One step of the first-run guide.</summary>
public sealed record GuideStep(string Title, string Detail, bool IsDone, string ActionText, ICommand Action)
{
    /// <summary>A word, so the state does not depend on seeing a colour or an icon.</summary>
    public string Mark => IsDone ? Loc.T("Done") : Loc.T("To do");
}

/// <summary>
/// Shown once, right after a new vault is created: the few steps that keep the vault safe and useful, each with a
/// button that goes to the right page. Every step can be skipped, and the codes page keeps reminding about what is left.
/// </summary>
public sealed class GuideViewModel(Func<IReadOnlyList<GuideStep>> steps, Action finish) : ViewModelBase
{
    /// <summary>The steps, with what is already done.</summary>
    public ObservableCollection<GuideStep> Steps { get; } = [];

    /// <summary>Leaves the guide.</summary>
    public ICommand FinishCommand { get; } = new RelayCommand(finish);

    /// <summary>Looks at what is done again.</summary>
    public void Refresh()
    {
        Steps.Clear();
        foreach (var step in steps())
        {
            Steps.Add(step);
        }
    }
}
