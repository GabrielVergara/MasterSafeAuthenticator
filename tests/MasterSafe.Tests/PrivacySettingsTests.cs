// File:    PrivacySettingsTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using MasterSafe.Platform;
using Xunit;

namespace MasterSafe.Tests;

[Collection("ClipboardPolicy")]
public sealed class PrivacySettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-privacy-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;

    public PrivacySettingsTests()
    {
        Directory.CreateDirectory(_folder);
        _store = new SettingsStore(Path.Combine(_folder, "settings.json"));
    }

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void SafeDefaults_LockOnSleepAndBlockCapture_ButDoNotLockOnMinimize()
    {
        var page = new SettingsViewModel(_store);

        page.LockOnSessionLock.Should().BeTrue();
        page.BlockScreenCapture.Should().BeTrue();
        page.LockOnMinimize.Should().BeFalse();
    }

    [Fact]
    public void TheThreeSwitches_AreRemembered()
    {
        var page = new SettingsViewModel(_store)
        {
            LockOnSessionLock = false,
            LockOnMinimize = true,
            BlockScreenCapture = false,
        };

        var again = new SettingsViewModel(_store);

        again.LockOnSessionLock.Should().BeFalse();
        again.LockOnMinimize.Should().BeTrue();
        again.BlockScreenCapture.Should().BeFalse();
        page.Should().NotBeNull();
    }

    [Fact]
    public void SettingsFromAnOlderVersion_GetTheSafeDefaults()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{\"AutoLockMinutes\": 3}");

        var settings = _store.Load();

        settings.LockOnSessionLock.Should().BeTrue();
        settings.BlockScreenCapture.Should().BeTrue();
        settings.UnlockFailures.Should().Be(0);
    }

    [Fact]
    public void TheSessionMonitor_StartsAndStopsCleanly()
    {
        using var monitor = new SessionMonitor();

        SessionMonitor.IsSupported.Should().Be(OperatingSystem.IsWindows());
    }

    private static PasswordEntry E(string folder, string title = "t") => new() { Title = title, Password = "p", Folder = folder };

    [Fact]
    public void TheFolderListWidth_FitsTheLongestName_WithinLimits()
    {
        var vm = new PasswordsViewModel();
        vm.Load([E("A")]);
        var narrow = vm.SuggestedFolderPaneWidth;
        narrow.Should().BeInRange(PasswordsViewModel.MinAutoPaneWidth, 230, "short names get a narrow list");

        vm.Load([E("A very long folder name for the personal accounts/Another quite long subfolder name")]);

        vm.SuggestedFolderPaneWidth.Should().Be(PasswordsViewModel.MaxAutoPaneWidth, "long names are capped");

        vm.Load([E("Email/Work")]);
        vm.SuggestedFolderPaneWidth.Should().BeInRange(PasswordsViewModel.MinAutoPaneWidth, PasswordsViewModel.MaxAutoPaneWidth);
        vm.EffectiveFolderPaneWidth.Should().Be(vm.SuggestedFolderPaneWidth);
    }

    [Fact]
    public void ADraggedWidth_IsUsed_AndRaisedForSaving_AndAResetFitsAgain()
    {
        var vm = new PasswordsViewModel();
        vm.Load([E("Email")]);
        var raised = 0;
        vm.FolderPaneWidthChanged += (_, _) => raised++;

        vm.UserFolderPaneWidth = 300;

        vm.EffectiveFolderPaneWidth.Should().Be(300);
        raised.Should().Be(1);

        vm.UserFolderPaneWidth = null;

        vm.EffectiveFolderPaneWidth.Should().Be(vm.SuggestedFolderPaneWidth);
        raised.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(5)]
    [InlineData(99999)]
    public void NonsenseWidths_AreIgnored(double width)
    {
        var vm = new PasswordsViewModel();
        vm.Load([E("Email")]);

        vm.UserFolderPaneWidth = width;
        vm.RestoreFolderPaneWidth(width);

        vm.UserFolderPaneWidth.Should().BeNull();
    }

    [Fact]
    public void ARestoredWidth_DoesNotCountAsAChange()
    {
        var vm = new PasswordsViewModel();
        var raised = 0;
        vm.FolderPaneWidthChanged += (_, _) => raised++;

        vm.RestoreFolderPaneWidth(280);

        vm.EffectiveFolderPaneWidth.Should().Be(280);
        raised.Should().Be(0);
    }
}
