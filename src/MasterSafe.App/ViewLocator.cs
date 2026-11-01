// File:    ViewLocator.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MasterSafe.App.ViewModels;

namespace MasterSafe.App;

/// <summary>
/// Maps a view model instance to its view by naming convention (â€¦ViewModel â†’ â€¦View).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "No data" };
        }

        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is not null)
        {
            var view = (Control)Activator.CreateInstance(type)!;

            // Give every input and heading a name a screen reader can speak, once the page has been laid out.
            view.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
                () => Services.AccessibilityNamer.Apply(view), Avalonia.Threading.DispatcherPriority.Loaded);
            return view;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
