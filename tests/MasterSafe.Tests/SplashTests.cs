// File:    SplashTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using FluentAssertions;
using MasterSafe.App;
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
}
