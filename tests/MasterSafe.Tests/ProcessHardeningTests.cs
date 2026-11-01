// File:    ProcessHardeningTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;

public sealed class ProcessHardeningTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-harden-" + Guid.NewGuid().ToString("N"));

    public ProcessHardeningTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void OnOtherSystems_NothingIsChanged_AndNothingFails()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessHardening.Apply("x").Should().Be(new HardeningResult(false, false, false));
        ProcessHardening.RestrictToCurrentUser(_folder).Should().BeFalse();
    }

    [Fact]
    public void ARestrictedFolder_StaysFullyUsableByTheCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllText(Path.Combine(_folder, "vault.sauth"), "existing");

        ProcessHardening.RestrictToCurrentUser(_folder).Should().BeTrue();

        File.ReadAllText(Path.Combine(_folder, "vault.sauth")).Should().Be("existing");
        File.WriteAllText(Path.Combine(_folder, "new.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "sub", "inner.txt"), "y");
    }

    [Fact]
    public void ARestrictedFolder_AllowsOnlyTheUserAndTheSystem_AndInheritsNothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessHardening.RestrictToCurrentUser(_folder);

        var security = new DirectoryInfo(_folder).GetAccessControl();
        security.AreAccessRulesProtected.Should().BeTrue("access is no longer inherited from the parent folder");
        var who = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => ((SecurityIdentifier)r.IdentityReference).Value)
            .Distinct()
            .ToList();
        using var me = WindowsIdentity.GetCurrent();
        who.Should().BeEquivalentTo(me.User!.Value, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value);
    }

    [Fact]
    public void AMissingFolder_IsNotAnError()
        => ProcessHardening.RestrictToCurrentUser(Path.Combine(_folder, "nope")).Should().BeFalse();

    [Fact]
    public void RestrictingTwice_IsHarmless()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessHardening.RestrictToCurrentUser(_folder).Should().BeTrue();
        ProcessHardening.RestrictToCurrentUser(_folder).Should().BeTrue();
        File.WriteAllText(Path.Combine(_folder, "still.txt"), "ok");
    }
}
