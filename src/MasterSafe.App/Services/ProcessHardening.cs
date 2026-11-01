// File:    ProcessHardening.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MasterSafe.App.Services;

/// <summary>What <see cref="ProcessHardening.Apply"/> managed to switch on.</summary>
/// <param name="ImageLoadRestricted">DLLs are only loaded from local, normal-integrity places, system folders first.</param>
/// <param name="ExtensionPointsDisabled">Legacy extension points (such as AppInit DLLs) are not loaded into the process.</param>
/// <param name="ErrorReportingExcluded">Windows Error Reporting does not collect memory dumps of this program.</param>
public sealed record HardeningResult(bool ImageLoadRestricted, bool ExtensionPointsDisabled, bool ErrorReportingExcluded);

/// <summary>
/// Windows protections that cost nothing in daily use. They make it harder to plant a DLL next to the program or in the
/// search path, and keep memory dumps of the unlocked vault out of the error-reporting system. Only mitigations that cannot
/// break the .NET runtime are used (code generation must stay allowed). A failure to switch one on is not an error: the
/// program simply runs without that protection.
/// </summary>
public static class ProcessHardening
{
    private const int ProcessExtensionPointDisablePolicy = 6;
    private const int ProcessImageLoadPolicy = 10;

    // PROCESS_MITIGATION_IMAGE_LOAD_POLICY: NoRemoteImages (1), NoLowMandatoryLabelImages (2), PreferSystem32Images (4).
    private const uint ImageLoadFlags = 1 | 2 | 4;

    // PROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY: DisableExtensionPoints (1).
    private const uint DisableExtensionPoints = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref uint flags, nuint length);

    [DllImport("wer.dll", CharSet = CharSet.Unicode)]
    private static extern int WerAddExcludedApplication(string exeName, [MarshalAs(UnmanagedType.Bool)] bool allUsers);

    /// <summary>
    /// Switches the protections on for this process. <paramref name="exeNameForErrorReporting"/> is the program's file name,
    /// for example "MasterSafe.App.exe"; null leaves error reporting alone (used by tests).
    /// </summary>
    public static HardeningResult Apply(string? exeNameForErrorReporting)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HardeningResult(false, false, false);
        }

        return ApplyOnWindows(exeNameForErrorReporting);
    }

    [SupportedOSPlatform("windows")]
    private static HardeningResult ApplyOnWindows(string? exeName)
    {
        var imageLoad = Try(() =>
        {
            var flags = ImageLoadFlags;
            return SetProcessMitigationPolicy(ProcessImageLoadPolicy, ref flags, sizeof(uint));
        });

        var extensions = Try(() =>
        {
            var flags = DisableExtensionPoints;
            return SetProcessMitigationPolicy(ProcessExtensionPointDisablePolicy, ref flags, sizeof(uint));
        });

        // Per user (no administrator needed): the exclusion is stored under the current user's registry key.
        var reporting = exeName is not null && Try(() => WerAddExcludedApplication(exeName, allUsers: false) >= 0);

        return new HardeningResult(imageLoad, extensions, reporting);
    }

    private static bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    /// <summary>
    /// Limits the folder to the current user and the system, removing the access other accounts and groups (such as
    /// Users) would otherwise inherit. Returns false when this is not Windows or the folder cannot be changed. The current
    /// user is always given full control first, so this can never lock the user out of their own files.
    /// </summary>
    public static bool RestrictToCurrentUser(string folder)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(folder))
        {
            return false;
        }

        return RestrictOnWindows(folder);
    }

    [SupportedOSPlatform("windows")]
    private static bool RestrictOnWindows(string folder)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is null)
            {
                return false;
            }

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(folder).SetAccessControl(security);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
