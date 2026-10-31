// File:    ClipboardSettingTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using Xunit;

namespace MasterSafe.Tests;

// The clipboard wait is process-wide, so these tests run one at a time and put it back afterwards.
[Collection("ClipboardPolicy")]
public sealed class ClipboardSettingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-clipset-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;

    public ClipboardSettingTests()
    {
        Directory.CreateDirectory(_folder);
        _store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        ClipboardPolicy.Use(ClipboardPolicy.DefaultSeconds);
    }

    public void Dispose()
    {
        ClipboardPolicy.Use(ClipboardPolicy.DefaultSeconds);
        Directory.Delete(_folder, true);
    }

    [Fact]
    public void ThereAreThreeChoices_AndTwoMinutesIsTheDefault()
    {
        ClipboardPolicy.Options.Select(o => o.Label).Should().Equal("30 seconds", "1 minute", "2 minutes");
        new SettingsViewModel(_store).SelectedClipboardDelay.Seconds.Should().Be(120);
        ClipboardPolicy.ClearAfter.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(30, "30 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(120, "2 minutes")]
    public void Choosing_AppliesAtOnce_ShowsInMessages_AndIsRemembered(int seconds, string text)
    {
        var page = new SettingsViewModel(_store);

        page.SelectedClipboardDelay = page.ClipboardDelays.Single(o => o.Seconds == seconds);

        ClipboardPolicy.ClearAfter.Should().Be(TimeSpan.FromSeconds(seconds));
        ClipboardPolicy.ClearAfterText.Should().Be(text);
        _store.Load().ClipboardClearSeconds.Should().Be(seconds);

        // After a restart the choice is still there.
        ClipboardPolicy.Use(ClipboardPolicy.DefaultSeconds);
        var again = new SettingsViewModel(_store);
        again.SelectedClipboardDelay.Seconds.Should().Be(seconds);
        ClipboardPolicy.ClearAfter.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(45)]
    [InlineData(86400)]
    public void ANonsenseValueInTheSettingsFile_FallsBackToTwoMinutes(int stored)
    {
        _store.Save(new AppSettings { ClipboardClearSeconds = stored });

        var page = new SettingsViewModel(_store);

        page.SelectedClipboardDelay.Seconds.Should().Be(120);
        ClipboardPolicy.ClearAfter.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SettingsFromAnOlderVersion_WithoutTheValue_UseTwoMinutes()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{\"AutoLockMinutes\": 7}");

        var page = new SettingsViewModel(_store);

        page.SelectedClipboardDelay.Seconds.Should().Be(120);
        page.AutoLockMinutes.Should().Be(7);
    }

    [Fact]
    public void ChangingTheDelay_KeepsOtherSettings()
    {
        _store.Save(new AppSettings { RecoveryPublicKey = "abc", AutoLockMinutes = 9 });
        var page = new SettingsViewModel(_store);

        page.SelectedClipboardDelay = page.ClipboardDelays[0];

        var saved = _store.Load();
        saved.RecoveryPublicKey.Should().Be("abc");
        saved.ClipboardClearSeconds.Should().Be(30);
    }

    [Fact]
    public void TheSchedulerUsesTheChosenWait()
    {
        ClipboardPolicy.Use(60);
        TimeSpan? used = null;
        var scheduler = new ClipboardClearScheduler((after, _) =>
        {
            used = after;
            return new Nothing();
        });

        scheduler.Schedule("x", () => Task.FromResult<string?>("x"), () => Task.CompletedTask);

        used.Should().Be(TimeSpan.FromMinutes(1));
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
