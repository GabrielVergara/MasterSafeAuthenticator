// File:    AccessibilityNamer.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace MasterSafe.App.Services;

/// <summary>
/// Gives screen readers the information they need. Every screen in the app lays out an input under a small label
/// ("Title", "User name"...). This finds that label and uses it as the accessible name of the input, marks headings
/// with their level, and names list boxes, progress bars and icon-only buttons, so a control can never be left without a
/// name by accident. A test runs it over every screen and fails when an interactive control still has no name.
/// </summary>
public static class AccessibilityNamer
{
    /// <summary>Names the controls under <paramref name="root"/> that do not have a name yet. Safe to call again.</summary>
    public static void Apply(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var visual in root.GetVisualDescendants().Prepend(root))
        {
            if (visual is not Control control)
            {
                continue;
            }

            switch (control)
            {
                case TextBlock text:
                    MarkHeading(text);
                    break;
                case ProgressBar bar:
                    Name(bar, ToolTipText(bar) ?? "Progress");
                    break;
                case ListBox list:
                    Name(list, ToolTipText(list) ?? "List");
                    break;
                case Button button when string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) && button.Content is not string:
                    Name(button, ToolTipText(button) ?? NameFromContent(button));
                    break;
                case TextBox or ComboBox or NumericUpDown or Slider or ToggleSwitch:
                    NameInput(control);
                    break;
            }
        }
    }

    /// <summary>True when an interactive control has a name a screen reader can speak.</summary>
    public static bool HasName(Control control)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
        {
            return true;
        }

        return control switch
        {
            Button { Content: string s } => !string.IsNullOrWhiteSpace(s),
            _ => false,
        };
    }

    private static void MarkHeading(TextBlock text)
    {
        if (text.Classes.Contains("h1"))
        {
            AutomationProperties.SetHeadingLevel(text, 1);
        }
        else if (text.Classes.Contains("h2"))
        {
            AutomationProperties.SetHeadingLevel(text, 2);
        }
    }

    private static void NameInput(Control input)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(input)))
        {
            return;
        }

        var name = LabelAbove(input) ?? (input as TextBox)?.PlaceholderText ?? ToolTipText(input);
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name(input, name);
        }
    }

    /// <summary>The text of the label that sits before the input in the same panel (or in a panel around it).</summary>
    private static string? LabelAbove(Control input)
    {
        Visual? current = input;
        for (var depth = 0; depth < 3 && current is not null; depth++)
        {
            if (current.GetVisualParent() is Panel panel)
            {
                var index = panel.Children.IndexOf((Control)current);
                for (var i = index - 1; i >= 0; i--)
                {
                    if (panel.Children[i] is TextBlock { Text: { Length: > 0 } text } block
                        && (block.Classes.Contains("label") || block.Classes.Contains("h2")))
                    {
                        return text;
                    }
                }
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static string? ToolTipText(Control control) => ToolTip.GetTip(control) as string;

    private static string NameFromContent(Button button)
        => button.Content is Control { } child && child.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text is { Length: > 0 } text
            ? text
            : "Button";

    private static void Name(Control control, string name)
    {
        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, name);
        }
    }
}
