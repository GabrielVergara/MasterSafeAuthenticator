using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SafeAuthenticator.App.ViewModels;

namespace SafeAuthenticator.App.Converters;

/// <summary>
/// Converts a <see cref="BackupProviderKind"/> to a friendly display label for the UI.
/// </summary>
public sealed class BackupProviderKindConverter : IValueConverter
{
    /// <summary>Shared instance for use from XAML via <c>x:Static</c>.</summary>
    public static readonly BackupProviderKindConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BackupProviderKind kind ? ToLabel(kind) : value?.ToString() ?? string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string label && string.Equals(label, ToLabel(BackupProviderKind.WebDav), StringComparison.Ordinal)
            ? BackupProviderKind.WebDav
            : BackupProviderKind.LocalFolder;

    private static string ToLabel(BackupProviderKind kind) => kind switch
    {
        BackupProviderKind.LocalFolder => "Local Folder",
        BackupProviderKind.WebDav => "WebDAV Cloud",
        _ => kind.ToString(),
    };
}
