// File:    AutoHideRevealTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Threading;
using FluentAssertions;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;

public sealed class AutoHideRevealTests
{
    [Fact]
    public void ARevealedPassword_IsHiddenAgainAfterTheDelay()
    {
        HeadlessUi.Run(() =>
        {
            var original = AutoHideReveal.Delay;
            AutoHideReveal.Delay = TimeSpan.FromMilliseconds(50);
            try
            {
                AutoHideReveal.Install();
                var window = new Window { Content = new TextBox { PasswordChar = '●' } };
                var box = (TextBox)window.Content;
                window.Show();

                box.RevealPassword = true;
                box.RevealPassword.Should().BeTrue();

                var until = DateTime.UtcNow.AddSeconds(5);
                while (box.RevealPassword && DateTime.UtcNow < until)
                {
                    Thread.Sleep(20);
                    Dispatcher.UIThread.RunJobs();
                }

                box.RevealPassword.Should().BeFalse("the password is hidden again by itself");
            }
            finally
            {
                AutoHideReveal.Delay = original;
            }
        });
    }
}
