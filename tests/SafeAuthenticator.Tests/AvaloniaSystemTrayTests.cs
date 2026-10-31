// File:    AvaloniaSystemTrayTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using FluentAssertions;
using SafeAuthenticator.App;
using SafeAuthenticator.Platform;
using Xunit;

namespace SafeAuthenticator.Tests;

public class AvaloniaSystemTrayTests
{
    [Fact]
    public void IsSupported_IsTrue()
    {
        using var tray = new AvaloniaSystemTray();

        tray.IsSupported.Should().BeTrue();
    }

    [Fact]
    public void RequestShowWindow_RaisesShowCommand()
    {
        using var tray = new AvaloniaSystemTray();
        string? command = null;
        tray.MenuItemClicked += (_, c) => command = c;

        tray.RequestShowWindow();

        command.Should().Be(TrayCommands.ShowWindow);
    }

    [Fact]
    public void RequestHideWindow_RaisesHideCommand()
    {
        using var tray = new AvaloniaSystemTray();
        string? command = null;
        tray.MenuItemClicked += (_, c) => command = c;

        tray.RequestHideWindow();

        command.Should().Be(TrayCommands.HideWindow);
    }
}
