// File:    NavigationTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public class NavigationTests
{
    [Fact]
    public void ActiveSection_FollowsCurrentPage()
    {
        var vm = new MainWindowViewModel();

        vm.ActiveSection.Should().BeEmpty();

        vm.Login.MasterPassword = "secret";
        vm.Login.UnlockCommand.Execute(null);
        vm.ActiveSection.Should().Be("Dashboard");

        vm.ShowPasswordsCommand.Execute(null);
        vm.ActiveSection.Should().Be("Passwords");

        vm.ShowAddAccountCommand.Execute(null);
        vm.ActiveSection.Should().Be("Add");

        vm.ShowSettingsCommand.Execute(null);
        vm.ActiveSection.Should().Be("Settings");
    }

    [Fact]
    public void Dashboard_IsEmpty_TracksAccounts()
    {
        var dashboard = new DashboardViewModel(startTimer: false);
        dashboard.IsEmpty.Should().BeTrue();

        var changed = new List<string?>();
        dashboard.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        dashboard.Load([new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }]);

        dashboard.IsEmpty.Should().BeFalse();
        changed.Should().Contain(nameof(DashboardViewModel.IsEmpty));
    }
}
