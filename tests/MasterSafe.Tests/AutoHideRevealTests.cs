// File:    AutoHideRevealTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using FluentAssertions;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;

public sealed class AutoHideRevealTests
{
    [Fact]
    public void ARevealedPassword_IsHiddenAgainAfterTheDelay()
    {
        var original = AutoHideReveal.Delay;
        AutoHideReveal.Delay = TimeSpan.FromMilliseconds(50);
        try
        {
            TextBox? box = null;
            HeadlessUi.Run(() =>
            {
                AutoHideReveal.Install();
                box = new TextBox { PasswordChar = '●' };
                new Window { Content = box }.Show();
                box.RevealPassword = true;
                box.RevealPassword.Should().BeTrue();
            });

            // The wait happens off the UI thread, so its loop is free to run the timer.
            var hidden = false;
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!hidden && DateTime.UtcNow < until)
            {
                Thread.Sleep(20);
                HeadlessUi.Run(() => hidden = !box!.RevealPassword);
            }

            hidden.Should().BeTrue("the password is hidden again by itself");
        }
        finally
        {
            AutoHideReveal.Delay = original;
        }
    }
}
