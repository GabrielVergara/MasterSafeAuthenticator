// File:    HelpAndAboutTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App;
using MasterSafe.App.Help;
using MasterSafe.App.ViewModels;
using MasterSafe.Platform;
using Xunit;

namespace MasterSafe.Tests;

public class HelpAndAboutTests
{
    [Fact]
    public void HelpContent_HasTopicsWithBodies()
    {
        HelpContent.Topics.Should().NotBeEmpty();
        HelpContent.Topics.Should().OnlyContain(t =>
            !string.IsNullOrWhiteSpace(t.Title)
            && !string.IsNullOrWhiteSpace(t.Summary)
            && !string.IsNullOrWhiteSpace(t.Body));
    }

    [Fact]
    public void HelpViewModel_SelectsFirstTopicByDefault()
    {
        var vm = new HelpViewModel();

        vm.Topics.Should().NotBeEmpty();
        vm.SelectedTopic.Should().Be(vm.Topics[0]);
    }

    [Fact]
    public void AboutViewModel_ExposesProductMetadata()
    {
        var vm = new AboutViewModel();

        vm.ProductName.Should().Be(AppInfo.ProductName);
        vm.Version.Should().NotBeNullOrWhiteSpace();
        vm.ProjectUrl.Should().Contain("github.com");
    }

    [Fact]
    public void MainWindow_ShowHelp_NavigatesToHelp()
    {
        var vm = new MainWindowViewModel();

        vm.ShowHelpCommand.Execute(null);

        vm.CurrentPage.Should().Be(vm.Help);
    }

    [Fact]
    public void MainWindow_ShowAbout_RaisesAboutRequested()
    {
        var vm = new MainWindowViewModel();
        var raised = false;
        vm.AboutRequested += (_, _) => raised = true;

        vm.ShowAboutCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void TrayDefaultMenu_IncludesAbout()
    {
        TrayCommands.DefaultMenu.Should().Contain(i => i.Id == TrayCommands.About);
    }
}
