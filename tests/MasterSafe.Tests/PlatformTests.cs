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
            providers.Select(p => p.SlotType).Should().Equal(SlotTypes.WindowsHello, SlotTypes.Fido2);
            providers.Select(p => p.DisplayName).Should().Equal("Windows Hello", "Security key");
            providers.Should().OnlyContain(p => p.EnrollmentHint.Length > 0);

            // Only asks whether the method can be used. It must not show a prompt or throw.
            foreach (var provider in providers)
            {
                var act = () => provider.IsAvailableAsync();
                await act.Should().NotThrowAsync();
            }
        }
        else
        {
            providers.Should().BeEmpty();
        }
    }
}
