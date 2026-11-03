// File:    EntryIcon.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.Views;

/// <summary>
/// The small picture beside an entry: the website icon the user accepted, or else the first letter of the name on a coloured
/// tile. It only decorates: the entry's name stays what a screen reader says, so it is hidden from them.
/// </summary>
public sealed class EntryIcon : Border
{
    /// <summary>Tile colours. White text on each must reach a contrast of 4.5 to 1; a test checks that.</summary>
    public static readonly IReadOnlyList<string> Palette = ["#1F6FB2", "#2E7D32", "#B3261E", "#6A1B9A", "#00695C", "#AD1457", "#4E5BA6", "#8A4B08"];

    /// <summary>The name of the entry.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<EntryIcon, string?>(nameof(Text));

    /// <summary>The accepted website icon (PNG, Base64), if any.</summary>
    public static readonly StyledProperty<string?> PngProperty = AvaloniaProperty.Register<EntryIcon, string?>(nameof(Png));

    private const int MaxStoredChars = 32 * 1024;

    private readonly TextBlock _letter = new()
    {
        Tag = "data",
        Foreground = Brushes.White,
        FontWeight = FontWeight.Bold,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Image _image = new() { Width = 32, Height = 32 };

    /// <summary>Creates the icon.</summary>
    public EntryIcon()
    {
        Width = 32;
        Height = 32;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        VerticalAlignment = VerticalAlignment.Top;
        ClipToBounds = true;
        Child = new Grid { Children = { _letter, _image } };
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        this[!BorderBrushProperty] = this.GetResourceObservable("TileBorderBrush").ToBinding();
    }

    /// <summary>The name of the entry.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The accepted website icon (PNG, Base64), if any.</summary>
    public string? Png
    {
        get => GetValue(PngProperty);
        set => SetValue(PngProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _letter.Text = LetterIcon.Letter(Text);
            Background = new SolidColorBrush(Color.Parse(Palette[LetterIcon.ColorIndex(Text)]));
        }
        else if (change.Property == PngProperty)
        {
            (_image.Source as IDisposable)?.Dispose();
            _image.Source = Decode(Png);
            _letter.IsVisible = _image.Source is null;
        }
    }

    private static Bitmap? Decode(string? png)
    {
        if (string.IsNullOrEmpty(png) || png.Length > MaxStoredChars)
        {
            return null;
        }

        try
        {
            // Whatever is stored goes through the same check as a download, and only the clean copy reaches the screen.
            var clean = IconImageCheck.Normalize(Convert.FromBase64String(png));
            if (clean is null)
            {
                return null;
            }

            using var stream = new MemoryStream(clean);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
