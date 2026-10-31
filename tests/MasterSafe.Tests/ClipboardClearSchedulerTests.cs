// File:    ClipboardClearSchedulerTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;

[Collection("ClipboardPolicy")]
public sealed class ClipboardClearSchedulerTests
{
    /// <summary>A clock and clipboard the test controls.</summary>
    private sealed class Fake
    {
        public readonly List<FakeTimer> Timers = [];
        public string? Clipboard;
        public int Clears;

        public ClipboardClearScheduler Scheduler { get; }

        public Fake() => Scheduler = new ClipboardClearScheduler((after, action) =>
        {
            var timer = new FakeTimer(after, action);
            Timers.Add(timer);
            return timer;
        });

        public void Copy(string text, TimeSpan? after = null)
        {
            Clipboard = text;
            Scheduler.Schedule(text, () => Task.FromResult<string?>(Clipboard), () =>
            {
                Clipboard = null;
                Clears++;
                return Task.CompletedTask;
            }, after);
        }
    }

    private sealed class FakeTimer(TimeSpan after, Func<Task> action) : IDisposable
    {
        public TimeSpan After { get; } = after;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        // A timer that was disposed may still have a tick in flight; it must do nothing.
        public Task FireAsync() => action();
    }

    [Fact]
    public void TheDefaultWaitIsTwoMinutes()
    {
        ClipboardPolicy.ClearAfter.Should().Be(TimeSpan.FromMinutes(2));
        ClipboardPolicy.ClearAfterText.Should().Be("2 minutes");

        var fake = new Fake();
        fake.Copy("secret");

        fake.Timers.Should().ContainSingle().Which.After.Should().Be(TimeSpan.FromMinutes(2));
        fake.Scheduler.IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task WhenTheTimeIsUp_TheSecretIsRemoved()
    {
        var fake = new Fake();
        fake.Copy("secret");

        await fake.Timers[0].FireAsync();

        fake.Clipboard.Should().BeNull();
        fake.Clears.Should().Be(1);
        fake.Scheduler.IsPending.Should().BeFalse();
    }

    [Fact]
    public async Task CopyingAnotherSecret_StartsTheFullWaitAgain_AndTheOldTimerDoesNothing()
    {
        var fake = new Fake();
        fake.Copy("first");

        fake.Copy("second");

        fake.Timers.Should().HaveCount(2);
        fake.Timers[0].Disposed.Should().BeTrue("the first timer was cancelled");
        fake.Timers[1].After.Should().Be(TimeSpan.FromMinutes(2), "the new copy gets the full time");

        await fake.Timers[0].FireAsync();
        fake.Clipboard.Should().Be("second", "a cancelled timer that still ticks must not clear the newer copy");
        fake.Clears.Should().Be(0);

        await fake.Timers[1].FireAsync();
        fake.Clipboard.Should().BeNull();
        fake.Clears.Should().Be(1);
    }

    [Fact]
    public async Task SomethingElseCopiedMeanwhile_IsNeverWiped()
    {
        var fake = new Fake();
        fake.Copy("secret");
        fake.Clipboard = "a shopping list";

        await fake.Timers[0].FireAsync();

        fake.Clipboard.Should().Be("a shopping list");
        fake.Clears.Should().Be(0);
    }

    [Fact]
    public async Task ABusyClipboard_IsNotAnError()
    {
        Captured? captured = null;
        var scheduler = new ClipboardClearScheduler((_, action) => captured = new Captured(action));

        scheduler.Schedule("x", () => throw new InvalidOperationException("busy"), () => Task.CompletedTask);

        var act = () => captured!.Action();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Cancel_StopsTheWait()
    {
        var fake = new Fake();
        fake.Copy("secret");

        fake.Scheduler.Cancel();
        await fake.Timers[0].FireAsync();

        fake.Timers[0].Disposed.Should().BeTrue();
        fake.Clipboard.Should().Be("secret");
        fake.Scheduler.IsPending.Should().BeFalse();
    }

    [Fact]
    public void ACustomWait_IsUsedWhenGiven()
    {
        var fake = new Fake();

        fake.Copy("secret", TimeSpan.FromSeconds(5));

        fake.Timers[0].After.Should().Be(TimeSpan.FromSeconds(5));
    }

    private sealed class Captured(Func<Task> action) : IDisposable
    {
        public Func<Task> Action { get; } = action;

        public void Dispose()
        {
        }
    }
}
