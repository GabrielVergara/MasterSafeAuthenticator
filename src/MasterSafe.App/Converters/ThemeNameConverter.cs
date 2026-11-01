// File:    ThemeNameConverter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using Avalonia.Data.Converters;

namespace MasterSafe.App.Converters;

/// <summary>Turns a theme into the words shown in the theme list.</summary>
public sealed class ThemeNameConverter : IValueConverter
{
    /// <summary>The shared instance used from XAML.</summary>
    public static ThemeNameConverter Instance { get; } = new();

    /// <summary>The text for a theme.</summary>
    public static string Describe(AppTheme theme) => theme switch
    {
        AppTheme.System => "Follow the system",
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        AppTheme.HighContrast => "High contrast",
        _ => theme.ToString(),
    };

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AppTheme theme ? Describe(theme) : value?.ToString();

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
