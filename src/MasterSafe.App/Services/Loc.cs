// File:    Loc.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using MasterSafe.App.Help;

namespace MasterSafe.App.Services;

/// <summary>
/// Shows the app in the user's language. The English text is the key: wherever a label, button, hint, tooltip or
/// message is set, a translation with exactly that text is substituted when one exists, otherwise the English text stays.
/// Messages with changing parts use <c>{0}</c> placeholders in the translation file, for example <c>Deleted {0}. =&gt; ...</c>.
/// The translation files are <c>Languages/es.txt</c> and <c>fi.txt</c>, embedded in the program.
/// </summary>
public static partial class Loc
{
    /// <summary>Language codes offered in Settings. <c>System</c> follows the computer.</summary>
    public static readonly IReadOnlyList<string> Choices = ["System", "en", "es", "fi"];

    private static Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private static List<(Regex Pattern, string Template)> _templates = [];
    private static Dictionary<string, HelpTopic> _help = new(StringComparer.Ordinal);
    private static bool _installed;

    /// <summary>The language in use: <c>en</c>, <c>es</c> or <c>fi</c>.</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>The name of a language, in that language, for the Settings list.</summary>
    public static string NameOf(string code) => code switch
    {
        "en" => "English",
        "es" => "Español",
        "fi" => "Suomi",
        _ => "System",
    };

    /// <summary>Resolves a setting (<c>System</c> or a code) to a language that has a translation file, or English.</summary>
    public static string Resolve(string? setting)
    {
        var code = string.IsNullOrEmpty(setting) || setting == "System"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : setting;
        return code is "es" or "fi" ? code : "en";
    }

    /// <summary>Selects the language and loads its translations. Views opened afterwards use it.</summary>
    public static void Use(string? setting)
    {
        Current = Resolve(setting);
        _exact = new Dictionary<string, string>(StringComparer.Ordinal);
        _templates = [];
        _help = new Dictionary<string, HelpTopic>(StringComparer.Ordinal);
        if (Current == "en")
        {
            return;
        }

        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Languages.{Current}.txt");
        if (stream is null)
        {
            return;
        }

        using (var reader = new StreamReader(stream))
        {
            Parse(reader.ReadToEnd());
        }

        using var help = typeof(Loc).Assembly.GetManifestResourceStream($"Languages.{Current}.help.txt");
        if (help is not null)
        {
            using var helpReader = new StreamReader(help);
            Parse(helpReader.ReadToEnd());
        }
    }

    /// <summary>Reads a translation file. Public so tests can check files.</summary>
    public static void Parse(string text)
    {
        var inHelp = false;
        string? title = null;
        string summary = string.Empty;
        var body = new List<string>();
        var inBody = false;

        void FlushTopic()
        {
            if (title is not null)
            {
                _help[title] = new HelpTopic(string.Empty, summary, string.Join('\n', body).Trim());
                _helpTitles[title] = _helpTitleValue;
            }

            title = null;
            body.Clear();
            inBody = false;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("@@help", StringComparison.Ordinal))
            {
                inHelp = true;
                continue;
            }

            if (!inHelp)
            {
                if (raw.Length == 0 || raw[0] == '#')
                {
                    continue;
                }

                var at = raw.IndexOf(" => ", StringComparison.Ordinal);
                if (at <= 0)
                {
                    continue;
                }

                var key = Unescape(raw[..at]);
                var value = Unescape(raw[(at + 4)..]);
                if (Placeholder().IsMatch(key))
                {
                    _templates.Add((ToPattern(key), value));
                }
                else
                {
                    _exact[key] = value;
                }

                continue;
            }

            if (raw.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushTopic();
                title = raw[4..].Trim();
                _helpTitleValue = title;
            }
            else if (title is not null && !inBody && raw.StartsWith("title: ", StringComparison.Ordinal))
            {
                _helpTitleValue = raw[7..].Trim();
            }
            else if (title is not null && !inBody && raw.StartsWith("summary: ", StringComparison.Ordinal))
            {
                summary = raw[9..].Trim();
            }
            else if (title is not null && !inBody && raw.StartsWith("body:", StringComparison.Ordinal))
            {
                inBody = true;
            }
            else if (inBody)
            {
                body.Add(raw);
            }
        }

        FlushTopic();
    }

    private static readonly Dictionary<string, string> _helpTitles = new(StringComparer.Ordinal);
    private static string _helpTitleValue = string.Empty;

    [GeneratedRegex(@"\{\d\}")]
    private static partial Regex Placeholder();

    private static string Unescape(string s) => s.Replace("\\n", "\n", StringComparison.Ordinal);

    private static Regex ToPattern(string key)
    {
        var parts = Placeholder().Split(key);
        var pattern = "^" + string.Join("(.+?)", parts.Select(Regex.Escape)) + "$";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    /// <summary>The translation of <paramref name="english"/>, or the text itself when there is none.</summary>
    public static string T(string english)
    {
        if (Current == "en" || string.IsNullOrEmpty(english))
        {
            return english;
        }

        return TryTranslate(english) ?? english;
    }

    private static string? TryTranslate(string english)
    {
        if (_exact.TryGetValue(english, out var exact))
        {
            return exact;
        }

        foreach (var (pattern, template) in _templates)
        {
            var m = pattern.Match(english);
            if (m.Success)
            {
                var result = template;
                for (var i = 1; i < m.Groups.Count; i++)
                {
                    result = result.Replace("{" + (i - 1) + "}", m.Groups[i].Value, StringComparison.Ordinal);
                }

                return result;
            }
        }

        return null;
    }

    /// <summary>The help topics in the current language; topics without a translation stay in English.</summary>
    public static IReadOnlyList<HelpTopic> LocalizeHelp(IReadOnlyList<HelpTopic> topics)
    {
        if (Current == "en")
        {
            return topics;
        }

        return topics.Select(t =>
        {
            if (!_help.TryGetValue(t.Title, out var h))
            {
                return t;
            }

            return new HelpTopic(_helpTitles.GetValueOrDefault(t.Title, t.Title), h.Summary.Length > 0 ? h.Summary : t.Summary, h.Body.Length > 0 ? HelpContent.Reflow(h.Body) : t.Body);
        }).ToList();
    }

    /// <summary>Starts translating text as controls receive it. Call once at start-up.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Watch(TextBlock.TextProperty);
        Watch(ContentControl.ContentProperty);
        Watch(TextBox.PlaceholderTextProperty);
        Watch(Window.TitleProperty);
        Watch(ToolTip.TipProperty);
        Watch(Avalonia.Automation.AutomationProperties.NameProperty);
    }

    private static void Watch<T>(AvaloniaProperty<T> property)
        => property.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<T>>(e =>
        {
            if (Current == "en" || e.Sender is not AvaloniaObject target || e.NewValue.GetValueOrDefault() is not string text)
            {
                return;
            }

            var translated = TryTranslate(text);
            if (translated is not null && translated != text)
            {
                target.SetCurrentValue(property, (T)(object)translated);
            }
        }));
}
