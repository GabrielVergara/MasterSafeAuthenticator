// File:    BackupProviderKindConverterTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Globalization;
using FluentAssertions;
using SafeAuthenticator.App.Converters;
using SafeAuthenticator.App.ViewModels;
using Xunit;

namespace SafeAuthenticator.Tests;

public class BackupProviderKindConverterTests
{
    private static readonly BackupProviderKindConverter Converter = BackupProviderKindConverter.Instance;

    [Theory]
    [InlineData(BackupProviderKind.LocalFolder, "Local Folder")]
    [InlineData(BackupProviderKind.WebDav, "WebDAV Cloud")]
    public void Convert_ProducesFriendlyLabel(BackupProviderKind kind, string expected)
    {
        var result = Converter.Convert(kind, typeof(string), null, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("WebDAV Cloud", BackupProviderKind.WebDav)]
    [InlineData("Local Folder", BackupProviderKind.LocalFolder)]
    public void ConvertBack_RoundTripsLabel(string label, BackupProviderKind expected)
    {
        var result = Converter.ConvertBack(label, typeof(BackupProviderKind), null, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }
}
