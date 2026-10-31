// File:    PlatformTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.Crypto;
using MasterSafe.Platform;
using Xunit;

namespace MasterSafe.Tests;

public class PlatformTests
{
    [Fact]
    public async Task UnlockProviders_MatchThePlatform()
    {
        var providers = PlatformServices.CreateUnlockProviders();

        if (OperatingSystem.IsWindows())
        {
            var hello = providers.Should().ContainSingle().Subject;
            hello.SlotType.Should().Be(SlotTypes.WindowsHello);
            hello.DisplayName.Should().Be("Windows Hello");

            // Only asks whether Hello can be used. It must not show a prompt or throw.
            var act = () => hello.IsAvailableAsync();
            await act.Should().NotThrowAsync();
        }
        else
        {
            providers.Should().BeEmpty();
        }
    }
}
