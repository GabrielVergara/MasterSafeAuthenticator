// File:    LanguageNameConverter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using Avalonia.Data.Converters;

namespace MasterSafe.App.Converters;

/// <summary>Turns a language code into its name, written in that language, for the language list.</summary>
public sealed class LanguageNameConverter : IValueConverter
{
    /// <summary>The shared instance used from XAML.</summary>
    public static LanguageNameConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value as string is { } code ? (code == "System" ? "Follow the system" : Services.Loc.NameOf(code)) : value?.ToString();

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
