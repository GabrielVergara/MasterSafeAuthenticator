// File:    AccessibilityTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.App.Views;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>Opens every page of the real application and checks what a screen reader would find.</summary>
public sealed class AccessibilityTests
{
    private static MainWindow OpenWindow(out MainWindowViewModel vm)
    {
        vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        window.Show();
        vm.Login.MasterPassword = "x";
        vm.Login.UnlockCommand.Execute(null);

        var now = DateTimeOffset.UtcNow;
        vm.Dashboard.Load(
        [
            new OtpAccount { Issuer = "GitHub", Label = "alice", Secret = "JBSWY3DPEHPK3PXP" },
            new OtpAccount { Issuer = "Mail", Label = "bob", Secret = "JBSWY3DPEHPK3PXP" },
            new OtpAccount { Issuer = "Shop", Label = "carol", Secret = "JBSWY3DPEHPK3PXP" },
            new OtpAccount { Issuer = "Bank", Label = "dave", Secret = "JBSWY3DPEHPK3PXP" },
        ]);
        vm.Passwords.Load(
        [
            new PasswordEntry { Title = "Gmail", UserName = "a@b.c", Password = "pw-1", Folder = "Email", Url = "example.com", ModifiedUtc = now },
            new PasswordEntry { Title = "Old", Password = "pw-1", ModifiedUtc = now.AddDays(-500) },
        ]);
        return window;
    }

    private static List<object> Pages(MainWindowViewModel vm) =>
    [
        vm.Dashboard, vm.Guide, vm.Passwords, vm.Health, vm.AccountEditor, vm.Import, vm.Backup, vm.Recovery, vm.Vaults, vm.Settings, vm.Help,
    ];

    private static void Show(MainWindow window, MainWindowViewModel vm, object page)
    {
        vm.CurrentPage = page;
        Dispatcher.UIThread.RunJobs();
        AccessibilityNamer.Apply(window);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void EveryInputAndButtonOnEveryPage_HasAName()
    {
        HeadlessUi.Run(() =>
        {
        var window = OpenWindow(out var vm);
        var offenders = new List<string>();

        foreach (var page in Pages(vm))
        {
            Show(window, vm, page);
            foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(c => c.IsVisible && c.IsEffectivelyVisible && c.TemplatedParent is null))
            {
                var interactive = control is Button or TextBox or ComboBox or NumericUpDown or Slider or ToggleSwitch or CheckBox;
                if (interactive && !AccessibilityNamer.HasName(control))
                {
                    offenders.Add($"{page.GetType().Name}: {control.GetType().Name} {AutomationProperties.GetAutomationId(control)} {(control as ContentControl)?.Content}");
                }
            }
        }

        offenders.Should().BeEmpty("a screen reader user must hear what each control is for");
        });
    }

    [Fact]
    public void EveryPageTitleIsAHeading_AndSectionsAreSubheadings()
    {
        HeadlessUi.Run(() =>
        {
        var window = OpenWindow(out var vm);

        foreach (var page in Pages(vm))
        {
            Show(window, vm, page);
            var headings = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("h1") && t.IsEffectivelyVisible).ToList();
            headings.Should().NotBeEmpty($"{page.GetType().Name} needs a page title");
            headings.Should().OnlyContain(t => AutomationProperties.GetHeadingLevel(t) == 1);
        }
        });
    }

    [Fact]
    public void ProgressBarsAndListsAreNamed()
    {
        HeadlessUi.Run(() =>
        {
        var window = OpenWindow(out var vm);

        foreach (var page in Pages(vm))
        {
            Show(window, vm, page);
            foreach (var bar in window.GetVisualDescendants().OfType<ProgressBar>().Where(b => b.IsEffectivelyVisible))
            {
                AutomationProperties.GetName(bar).Should().NotBeNullOrWhiteSpace();
            }

            foreach (var list in window.GetVisualDescendants().OfType<ListBox>().Where(l => l.IsEffectivelyVisible))
            {
                AutomationProperties.GetName(list).Should().NotBeNullOrWhiteSpace();
            }
        }
        });
    }

    [Fact]
    public void StatusMessagesAreAnnouncedPolitely()
    {
        HeadlessUi.Run(() =>
        {
        var window = OpenWindow(out var vm);
        Show(window, vm, vm.Passwords);

        vm.Passwords.Status = "A message for the screen reader";
        Dispatcher.UIThread.RunJobs();
        var status = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "A message for the screen reader");

        AutomationProperties.GetLiveSetting(status).Should().Be(AutomationLiveSetting.Polite);
        });
    }

    [Fact]
    public void TheSidebarHasAccessKeys_AndTheLockWarningIsAssertive()
    {
        HeadlessUi.Run(() =>
        {
        var window = OpenWindow(out var vm);
        vm.LockWarningSeconds = 20;
        Dispatcher.UIThread.RunJobs();

        var buttons = window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).Where(c => c is not null).ToList();
        buttons.Should().Contain(["_Passwords", "_Settings", "_Lock vault", "Help (F1)"]);

        var warning = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == vm.LockWarningText);
        AutomationProperties.GetLiveSetting(warning).Should().Be(AutomationLiveSetting.Assertive);
        });
    }

    [Fact]
    public void TheWindowScalesWithTheTextSizeSetting()
    {
        HeadlessUi.Run(() =>
        {
        var window = OpenWindow(out var vm);
        var zoom = window.GetVisualDescendants().OfType<LayoutTransformControl>().First(c => c.Name == "ZoomHost");

        vm.Settings.TextScalePercent = 150;
        Dispatcher.UIThread.RunJobs();

        vm.TextScale.Should().Be(1.5);
        zoom.LayoutTransform.Should().NotBeNull();
        ((Avalonia.Media.ScaleTransform)zoom.LayoutTransform!).ScaleX.Should().Be(1.5);
        });
    }
}
