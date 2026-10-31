// File:    CredentialStoreTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using FluentAssertions;
using SafeAuthenticator.Platform;
using Xunit;

namespace SafeAuthenticator.Tests;

public class CredentialStoreTests
{
    [Fact]
    public void Headless_SaveRead_RoundTrips_AndCopies()
    {
        var store = new HeadlessCredentialStore();
        var secret = new byte[] { 1, 2, 3 };

        store.Save("t", secret);
        secret[0] = 9;

        store.IsSupported.Should().BeFalse();
        store.Read("t").Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Headless_MissingAndDelete()
    {
        var store = new HeadlessCredentialStore();

        store.Read("none").Should().BeNull();
        store.Save("t", [1]);
        store.Delete("t").Should().BeTrue();
        store.Delete("t").Should().BeFalse();
        store.Read("t").Should().BeNull();
    }

    [Fact]
    public void Windows_RoundTrip_UsesRealCredentialManager()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore();
        var target = "SafeAuthenticator.Tests." + Guid.NewGuid();
        byte[] secret = [10, 20, 30, 40];

        try
        {
            store.Save(target, secret);
            store.Read(target).Should().Equal(secret);
        }
        finally
        {
            store.Delete(target);
        }

        store.Read(target).Should().BeNull();
    }
}
