// File:    SplashTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using FluentAssertions;
using MasterSafe.App;
using MasterSafe.App.Services;
using MasterSafe.App.Views;
using Xunit;

namespace MasterSafe.Tests;

public class SplashTests
{
    [Fact]
    public void Splash_ShowsProductNameAndAuthor()
    {
        AppInfo.ProductName.Should().Be("MasterSafe Authenticator");
        AppInfo.SplashByline.Should().Be("by Gabriel Vergara Ezcurdia");
        AppInfo.SplashVersion.Should().StartWith("Version ");
    }

    [Fact]
    public void Copyright_UsesCopyrightSignAndAuthor()
    {
        AppInfo.Copyright.Should().StartWith("© ").And.EndWith(AppInfo.Author);
    }

    [Fact]
    public void TheLogoIntro_SweepsTheRingFirst_ThenShowsTheKeyhole_AndEndsOnTheStaticLogo()
    {
        LogoMark.ArcLength(0).Should().Be(0);
        LogoMark.ArcLength(0.5).Should().BeApproximately(0.35, 0.001, "the ring is half drawn half way through");
        LogoMark.ArcLength(1).Should().BeApproximately(0.70, 0.001, "the logo's arc covers 70 % of the ring");
        LogoMark.ArcLength(0.3).Should().BeLessThan(LogoMark.ArcLength(0.6));

        LogoMark.KeyholeShown(0.5).Should().Be(0, "the keyhole waits for the ring");
        LogoMark.KeyholeShown(0.75).Should().BeInRange(0.1, 0.99);
        LogoMark.KeyholeShown(1).Should().Be(1);

        SplashWindow.IntroDuration.Should().Be(TimeSpan.FromMilliseconds(1600));
        new LogoMark().Progress.Should().Be(1, "anywhere else the logo is still");
    }

    [Fact]
    public void TheSplash_PlaysTheIntro_UnlessAnimationsAreOff()
    {
        var saved = MotionPreference.IsReduced;
        try
        {
            HeadlessUi.Run(() =>
            {
                MotionPreference.IsReduced = () => false;
                new SplashWindow().Logo.Progress.Should().Be(0, "the intro starts from an empty ring");

                MotionPreference.IsReduced = () => true;
                new SplashWindow().Logo.Progress.Should().Be(1, "with animations off, the finished logo shows at once");
            });
        }
        finally
        {
            MotionPreference.IsReduced = saved;
        }
    }
}
