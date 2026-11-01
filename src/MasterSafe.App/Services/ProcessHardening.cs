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
/// <param name="MemoryAccessBlocked">Other programs of the same user cannot simply open this one to read its memory.</param>
public sealed record HardeningResult(bool ImageLoadRestricted, bool ExtensionPointsDisabled, bool ErrorReportingExcluded, bool MemoryAccessBlocked = false);

/// <summary>
/// Windows protections that cost nothing in daily use. They make it harder to plant a DLL next to the program or in the
/// search path, and keep memory dumps of the unlocked vault out of the error-reporting system. Only mitigations that cannot
/// break the .NET runtime are used (code generation must stay allowed). A failure to switch one on is not an error: the
/// program simply runs without that protection.
/// <para>
/// Blocking memory access stops ordinary reads, not a determined attacker: a program running as an administrator gets past
/// it, and so can one that first rewrites the permission.
/// </para>
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

    private const uint DaclSecurityInformation = 4;

    // The user may see that the program runs, wait for it and end it, also with taskkill or Task Manager if it ever hangs:
    // SYNCHRONIZE, QUERY_INFORMATION, QUERY_LIMITED_INFORMATION and TERMINATE. None of these can read or change memory.
    private const uint OwnerProcessAccess = 0x00100000 | 0x0400 | 0x1000 | 0x0001;

    // Set once the memory block is on. The Windows Hello prompt needs the normal permission back for a moment.
    private static byte[]? _normalPermission;
    private static byte[]? _blockedPermission;

    private const int LinuxSetDumpable = 4;
    private const int LinuxCoreLimit = 4;

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(nint handle, uint information, byte[] descriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKernelObjectSecurity(nint handle, uint information, byte[]? descriptor, uint length, out uint needed);

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static extern int LinuxProcessControl(int option, nuint value, nuint unused2, nuint unused3, nuint unused4);

    [DllImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
    private static extern int LinuxSetLimit(int resource, ref LinuxLimit limit);

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxLimit
    {
        public nuint Current;
        public nuint Maximum;
    }

    /// <summary>
    /// Switches the protections on for this process. <paramref name="exeNameForErrorReporting"/> is the program's file name,
    /// for example "MasterSafe.App.exe"; null leaves error reporting alone (used by tests).
    /// </summary>
    public static HardeningResult Apply(string? exeNameForErrorReporting)
    {
        if (OperatingSystem.IsLinux())
        {
            return new HardeningResult(false, false, false, BlockMemoryAccessOnLinux());
        }

        if (!OperatingSystem.IsWindows())
        {
            return new HardeningResult(false, false, false);
        }

        return ApplyOnWindows(exeNameForErrorReporting);
    }

    /// <summary>No core dumps, and programs of the same user can no longer attach to this one or read its memory.</summary>
    private static bool BlockMemoryAccessOnLinux()
    {
        try
        {
            var noCoreDumps = new LinuxLimit();
            var limited = LinuxSetLimit(LinuxCoreLimit, ref noCoreDumps) == 0;
            return LinuxProcessControl(LinuxSetDumpable, 0, 0, 0, 0) == 0 && limited;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>The permission set on this process: full access for the system, and only what is needed to end it for the user.</summary>
    public static string ProcessPermission(string userSid) => $"D:P(A;;0x1FFFFF;;;SY)(A;;0x{OwnerProcessAccess:X};;;{userSid})";

    /// <summary>Stops other programs of the user from opening this one. Keeps the normal permission for <see cref="OpenForSystemPrompt"/>.</summary>
    [SupportedOSPlatform("windows")]
    public static bool BlockMemoryAccessOnWindows()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not { } user)
            {
                return false;
            }

            var descriptor = new RawSecurityDescriptor(ProcessPermission(user.Value));
            var binary = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(binary, 0);

            GetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, null, 0, out var needed);
            var normal = new byte[needed];
            if (!GetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, normal, needed, out _)
                || !SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, binary))
            {
                return false;
            }

            _normalPermission = normal;
            _blockedPermission = binary;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gives the process its normal permission until the result is disposed. The Windows Hello prompt belongs
    /// to Windows and can fail after verifying the user if it cannot reach this program. Does nothing while the block is off.
    /// </summary>
    public static IDisposable OpenForSystemPrompt()
    {
        if (_normalPermission is { } normal && _blockedPermission is { } blocked && SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, normal))
        {
            return new SystemPromptScope(blocked);
        }

        return new SystemPromptScope(null);
    }

    private sealed class SystemPromptScope(byte[]? permissionToRestore) : IDisposable
    {
        private byte[]? _permissionToRestore = permissionToRestore;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _permissionToRestore, null) is { } permission)
            {
                SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, permission);
            }
        }
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

        return new HardeningResult(imageLoad, extensions, reporting, BlockMemoryAccessOnWindows());
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
