// File:    ContrastTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>
/// Checks the colour pairs the app's themes use against WCAG 2.2: 4.5:1 for normal text. The colours are read from
/// <c>App.axaml</c> itself, so a colour that is changed later is checked as well.
/// </summary>
public sealed partial class ContrastTests
{
    private static readonly string AppXaml = File.ReadAllText(Path.Combine(
        Path.GetDirectoryName(SourceFile())!, "..", "..", "src", "MasterSafe.App", "App.axaml"));

    private static string SourceFile([CallerFilePath] string path = "") => path;

    private const double NormalText = 4.5;

    // ---------------------------------------------------------------- colour maths (WCAG 2.2, relative luminance)

    private static (double R, double G, double B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        return (
            int.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static double Channel(double c)
    {
        var s = c / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static double Luminance((double R, double G, double B) c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Ratio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>The colour that shows when <paramref name="foreground"/> is drawn with the given opacity over <paramref name="background"/>.</summary>
    private static (double R, double G, double B) Blend((double R, double G, double B) foreground, (double R, double G, double B) background, double opacity)
        => (
            (foreground.R * opacity) + (background.R * (1 - opacity)),
            (foreground.G * opacity) + (background.G * (1 - opacity)),
            (foreground.B * opacity) + (background.B * (1 - opacity)));

    // ---------------------------------------------------------------- reading the themes

    private static (double R, double G, double B) Brush(string theme, string key)
    {
        var section = Regex.Match(AppXaml, $@"<ResourceDictionary x:Key=""{Regex.Escape(theme)}"">(.*?)</ResourceDictionary>", RegexOptions.Singleline);
        section.Success.Should().BeTrue($"theme {theme} exists");
        var color = Regex.Match(section.Groups[1].Value, $@"x:Key=""{key}"" Color=""(#[0-9A-Fa-f]{{6}})""");
        color.Success.Should().BeTrue($"{key} is defined for {theme}");
        return Parse(color.Groups[1].Value);
    }

    private static (double R, double G, double B) Palette(string theme, string attribute)
    {
        var m = Regex.Match(AppXaml, $@"<ColorPaletteResources x:Key=""{theme}""[^>]*?\b{attribute}=""(#[0-9A-Fa-f]{{6}})""");
        m.Success.Should().BeTrue($"{attribute} is defined for {theme}");
        return Parse(m.Groups[1].Value);
    }

    private static double Opacity(string styleSelector)
    {
        var m = Regex.Match(AppXaml, $@"<Style Selector=""{Regex.Escape(styleSelector)}"">(.*?)</Style>", RegexOptions.Singleline);
        var o = Regex.Match(m.Groups[1].Value, @"Property=""Opacity"" Value=""([0-9.]+)""");
        return o.Success ? double.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture) : 1.0;
    }

    public static IEnumerable<object[]> Themes => [["Light"], ["Dark"]];

    [Theory]
    [MemberData(nameof(Themes))]
    public void NormalTextOnACard_IsEasyToRead(string theme)
        => Ratio(Brush(theme, "CodeBrush"), Brush(theme, "CardBrush")).Should().BeGreaterThanOrEqualTo(7.0, "body text should reach AAA");

    [Theory]
    [MemberData(nameof(Themes))]
    public void HintsAndLabels_StayAboveTheMinimum_OnCardsAndOnThePage(string theme)
    {
        var text = Brush(theme, "CodeBrush");
        var card = Brush(theme, "CardBrush");
        var page = Palette(theme, "RegionColor");

        foreach (var (style, name) in new[] { ("TextBlock.hint", "hint"), ("TextBlock.label", "label") })
        {
            var opacity = Opacity(style);
            Ratio(Blend(text, card, opacity), card).Should().BeGreaterThanOrEqualTo(NormalText, $"{name} text on a card in {theme}");
            Ratio(Blend(text, page, opacity), page).Should().BeGreaterThanOrEqualTo(NormalText, $"{name} text on the page in {theme}");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void ErrorTextOnACard_IsReadable(string theme)
        => Ratio(Brush(theme, "DangerBrush"), Brush(theme, "CardBrush")).Should().BeGreaterThanOrEqualTo(NormalText);

    [Fact]
    public void TheButtonText_OnTheAccentColour_IsReadable()
    {
        // Light theme buttons use white text on the accent; dark theme buttons use black text on the lighter accent.
        Ratio(Parse("#FFFFFF"), Palette("Light", "Accent")).Should().BeGreaterThanOrEqualTo(NormalText);
        Ratio(Parse("#000000"), Palette("Dark", "Accent")).Should().BeGreaterThanOrEqualTo(NormalText);
    }

    [Fact]
    public void TheSidebar_TextIsReadable()
    {
        var sidebar = Parse("#12263A");
        Ratio(Parse("#FFFFFF"), sidebar).Should().BeGreaterThanOrEqualTo(NormalText);
        Ratio(Parse("#8FA6B8"), sidebar).Should().BeGreaterThanOrEqualTo(NormalText, "the small grey subtitle");
    }

    [Fact]
    public void TheHighContrastTheme_IsBlackAndWhiteWithAStrongWarningColour()
    {
        Ratio(Brush("{x:Static local:App.HighContrastVariant}", "CodeBrush"), Brush("{x:Static local:App.HighContrastVariant}", "CardBrush")).Should().BeGreaterThanOrEqualTo(15);
        Ratio(Brush("{x:Static local:App.HighContrastVariant}", "DangerBrush"), Brush("{x:Static local:App.HighContrastVariant}", "CardBrush")).Should().BeGreaterThanOrEqualTo(7);
        Ratio(Brush("{x:Static local:App.HighContrastVariant}", "CardBorderBrush"), Brush("{x:Static local:App.HighContrastVariant}", "CardBrush")).Should().BeGreaterThanOrEqualTo(15, "borders are plainly visible");
    }

    [Fact]
    public void TheMathMatchesKnownValues()
    {
        Ratio(Parse("#000000"), Parse("#FFFFFF")).Should().BeApproximately(21.0, 0.01);
        Ratio(Parse("#777777"), Parse("#FFFFFF")).Should().BeApproximately(4.48, 0.02);
    }
}
