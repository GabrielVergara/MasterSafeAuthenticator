// File:    SecretClipboardTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Input;
using FluentAssertions;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;

public sealed class SecretClipboardTests
{
    [Fact]
    public void TheSecretIsStillThereAsPlainText()
    {
        var transfer = SecretClipboard.Create("s3cret!");

        var item = transfer.Items.Should().ContainSingle().Subject;
        item.Formats.Should().Contain(DataFormat.Text);
        item.TryGetRaw(DataFormat.Text).Should().Be("s3cret!");
    }

    [Fact]
    public void OnWindows_TheCopyIsKeptOutOfHistoryAndTheCloud()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var item = SecretClipboard.Create("s3cret!").Items[0];

        // The two history and cloud markers must be a DWORD 0 ("no"); the monitor marker only has to be present.
        item.TryGetRaw(DataFormat.CreateBytesPlatformFormat(SecretClipboard.WindowsHistoryFormat)).Should().BeEquivalentTo(new byte[] { 0, 0, 0, 0 });
        item.TryGetRaw(DataFormat.CreateBytesPlatformFormat(SecretClipboard.WindowsCloudFormat)).Should().BeEquivalentTo(new byte[] { 0, 0, 0, 0 });
        item.TryGetRaw(DataFormat.CreateBytesPlatformFormat(SecretClipboard.WindowsMonitorFormat)).Should().NotBeNull();
        SecretClipboard.MarkerNames.Should().HaveCount(3);
    }

    [Fact]
    public void OnLinux_TheCopyIsMarkedAsComingFromAPasswordManager()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var item = SecretClipboard.Create("s3cret!").Items[0];

        item.TryGetRaw(DataFormat.CreateStringPlatformFormat(SecretClipboard.LinuxPasswordHintFormat)).Should().Be("secret");
    }

    [Fact]
    public void EveryMarkerThatIsNamedIsReallyAdded()
    {
        var item = SecretClipboard.Create("x").Items[0];

        item.Formats.Count.Should().Be(1 + SecretClipboard.MarkerNames.Count);
    }
}
