// File:    LocalizationTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using FluentAssertions;
using MasterSafe.App.Help;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;


public sealed partial class LocalizationTests : IDisposable
{
    private static string SourceFile([CallerFilePath] string path = "") => path;

    private static string AppFolder => Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "src", "MasterSafe.App");

    public void Dispose() => Loc.Use("en");

    /// <summary>Texts set in XAML that the screens show. Pieces of a sentence built from several parts are not translated as a whole.</summary>
    private static IEnumerable<string> XamlTexts()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(AppFolder, "Views"), "*.axaml").Append(Path.Combine(AppFolder, "App.axaml")))
        {
            var xaml = File.ReadAllText(file);
            foreach (Match m in XamlAttribute().Matches(xaml))
            {
                var value = m.Groups[2].Value.Replace("&amp;", "&", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal);
                if (Letters().IsMatch(value) && value != "Delete " && !value.StartsWith("? ", StringComparison.Ordinal) && value != "Delete selected (")
                {
                    found.Add(value);
                }
            }
        }

        return found;
    }

    [GeneratedRegex(@"\s(Text|Content|PlaceholderText|ToolTip\.Tip|Header|Title)=""([^""{][^""]*)""")]
    private static partial Regex XamlAttribute();

    [GeneratedRegex("[A-Za-z]{2}")]
    private static partial Regex Letters();

    private static HashSet<string> Keys(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Languages.{code}.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n").Split('\n')
            .Where(l => l.Length > 0 && l[0] != '#' && l.Contains(" => ", StringComparison.Ordinal))
            .Select(l => l[..l.IndexOf(" => ", StringComparison.Ordinal)])
            .ToHashSet();
    }

    // Texts that are the same in every language (names, product words, examples).
    private static readonly string[] Neutral =
    [
        "MasterSafe", "MasterSafe Authenticator", "https://", "otpauth://totp/Example:name@example.com?secret=...",
        "https://cloud.example.com/remote.php/dav/files/you/backups/", "recovery-....sauth", "MasterSafe-recovery-key-....msrk",
        "Gabriel Vergara Ezcurdia",
    ];

    [Theory]
    [InlineData("es")]
    [InlineData("fi")]
    public void EveryTextOnEveryScreen_HasATranslation(string code)
    {
        var keys = Keys(code);
        var missing = XamlTexts().Where(t => !keys.Contains(t) && !Neutral.Contains(t) && !t.StartsWith("http", StringComparison.Ordinal)).ToList();
        missing.Should().BeEmpty($"these texts need a line in Languages/{code}.txt");
    }

    [Fact]
    public void BothLanguages_TranslateTheSameTexts()
        => Keys("es").Should().BeEquivalentTo(Keys("fi"));

    [Theory]
    [InlineData("es")]
    [InlineData("fi")]
    public void AccessKeysAndPlaceholders_AreKept(string code)
    {
        Loc.Use(code);
        foreach (var key in Keys(code).Where(k => k.StartsWith('_') || k.Contains('_') && k.Length < 25))
        {
            Loc.T(key).Should().Contain("_", $"'{key}' has an access key");
        }

        Loc.T("Deleted GitHub.").Should().Contain("GitHub");
        Loc.T("3 of 8 settings could be stronger.").Should().Contain("3").And.Contain("8");
        Loc.T("Something with no translation at all").Should().Be("Something with no translation at all");
    }

    [Fact]
    public void ControlsReceiveTranslatedText_WhateverWayTheTextIsSet()
    {
        HeadlessUi.Run(() =>
        {
            Loc.Install();
            Loc.Use("fi");

            var button = new Button { Content = "Cancel" };
            var label = new TextBlock();
            var box = new TextBox { PlaceholderText = "Type a name" };
            var window = new Window { Title = "Settings", Content = new StackPanel { Children = { button, label, box } } };
            label.Text = "Deleted GitHub.";
            ToolTip.SetTip(button, "Copy code");

            button.Content.Should().Be("Peruuta");
            label.Text.Should().Be("GitHub poistettu.");
            box.PlaceholderText.Should().Be("Kirjoita nimi");
            window.Title.Should().Be("Asetukset");
            ToolTip.GetTip(button).Should().Be("Kopioi koodi");

            Loc.Use("en");
            new Button { Content = "Cancel" }.Content.Should().Be("Cancel");
        });
    }

    [Fact]
    public void English_IsLeftAlone()
    {
        Loc.Use("en");
        Loc.T("Settings").Should().Be("Settings");
        Loc.Resolve("fr").Should().Be("en");
        Loc.Resolve("fi").Should().Be("fi");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("fi")]
    public void EveryHelpTopic_IsTranslated(string code)
    {
        Loc.Use(code);

        var topics = Loc.LocalizeHelp(HelpContent.Topics);

        topics.Should().HaveCount(HelpContent.Topics.Count);
        for (var i = 0; i < topics.Count; i++)
        {
            topics[i].Body.Should().NotBe(HelpContent.Topics[i].Body, $"help topic '{HelpContent.Topics[i].Title}' needs a translation");
            topics[i].Title.Should().NotBeNullOrWhiteSpace();
        }
    }
}
