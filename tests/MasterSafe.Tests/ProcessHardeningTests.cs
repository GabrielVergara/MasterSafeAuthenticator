// File:    ProcessHardeningTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.Crypto;
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

        ProcessHardening.Apply("x").Should().Be(new HardeningResult(false, false, false, OperatingSystem.IsLinux()));
        ProcessHardening.RestrictToCurrentUser(_folder).Should().BeFalse();
    }

    [Fact]
    public void TheProcessPermission_GivesTheUserNoWayToReadMemory()
    {
        const int ReadMemory = 0x0010;
        const int WriteMemory = 0x0020;
        const int ChangePermissions = 0x00040000;
        const int CreateThread = 0x0002;
        const string User = "S-1-5-21-1-2-3-1001";

        var permission = ProcessHardening.ProcessPermission(User);

        var access = Convert.ToInt32(permission.Split(User)[0].Split("0x")[^1].TrimEnd(';'), 16);
        (access & (ReadMemory | WriteMemory | ChangePermissions | CreateThread)).Should().Be(0);
        permission.Should().StartWith("D:P", "inherited permissions must not add anything back");
    }

    [Fact]
    public void WindowsPrompts_LiftTheMemoryBlockOnlyWhileTheyAreOpen()
    {
        // The block does not stop an administrator, so the check means nothing on such an account (GitHub's test machines).
        if (!OperatingSystem.IsWindows() || new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return;
        }

        ProcessHardening.BlockMemoryAccessOnWindows().Should().BeTrue();
        CanReadMyMemory().Should().BeFalse();

        var prompt = ProcessHardening.OpenForSystemPrompt();
        CanReadMyMemory().Should().BeTrue();

        prompt.Dispose();
        CanReadMyMemory().Should().BeFalse();

        prompt.Dispose();
        CanReadMyMemory().Should().BeFalse();
    }

    [Fact]
    public async Task APromptProvider_PassesEverythingThrough_EvenWhenThePromptFails()
    {
        var provider = new SystemPromptProvider(new StubProvider());

        provider.SlotType.Should().Be("stub");
        provider.DisplayName.Should().Be("Stub");
        provider.EnrollmentHint.Should().Be("hint");
        (await provider.IsAvailableAsync()).Should().BeTrue();
        (await provider.DeriveAsync("slot", [])).Should().Equal(1);
        (await provider.EnrollAsync("slot")).Key.Should().Equal(2);
        var failing = () => provider.DeriveAsync("fail", []);
        await failing.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class StubProvider : ISecretProvider
    {
        public string SlotType => "stub";

        public string DisplayName => "Stub";

        public string EnrollmentHint => "hint";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<SecretEnrollment> EnrollAsync(string slotId, CancellationToken cancellationToken = default)
            => Task.FromResult(new SecretEnrollment([], [2]));

        public Task<byte[]> DeriveAsync(string slotId, byte[] enrollmentData, CancellationToken cancellationToken = default)
            => slotId == "fail" ? throw new InvalidOperationException("failed") : Task.FromResult<byte[]>([1]);

        public Task RemoveAsync(string slotId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private static bool CanReadMyMemory()
    {
        const uint ReadMemory = 0x0010;
        var handle = OpenProcess(ReadMemory, false, Environment.ProcessId);
        if (handle == 0)
        {
            return false;
        }

        CloseHandle(handle);
        return true;
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

        var who = AllowedSids(_folder);
        using var me = WindowsIdentity.GetCurrent();
        who.Should().BeEquivalentTo(me.User!.Value, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> AllowedSids(string folder)
    {
        var security = new DirectoryInfo(folder).GetAccessControl();
        security.AreAccessRulesProtected.Should().BeTrue("access is no longer inherited from the parent folder");
        return security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => ((SecurityIdentifier)r.IdentityReference).Value)
            .Distinct()
            .ToList();
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
