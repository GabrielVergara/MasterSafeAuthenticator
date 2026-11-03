// File:    BulkObservableCollection.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// A collection that can change many items with one notification. Adding items one at a time makes every listener react to
/// every item, which takes minutes with thousands of entries.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces everything in one step.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items.ToList();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>Adds several items in one step.</summary>
    public void AddRange(IEnumerable<T> items) => ReplaceAll(this.Concat(items));
}
