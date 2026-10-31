// File:    DpapiProtector.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace SafeAuthenticator.Crypto;

/// <summary>
/// Optional OS-level protection of sensitive blobs using Windows DPAPI.
/// On non-Windows platforms the methods throw <see cref="PlatformNotSupportedException"/>.
/// </summary>
public static class DpapiProtector
{
    /// <summary>True when DPAPI is available on the current OS.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Protects data for the current user using DPAPI.</summary>
    [SupportedOSPlatform("windows")]
    public static byte[] Protect(byte[] data, byte[]? entropy = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        return ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
    }

    /// <summary>Unprotects data previously protected with <see cref="Protect"/>.</summary>
    [SupportedOSPlatform("windows")]
    public static byte[] Unprotect(byte[] data, byte[]? entropy = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        return ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
    }
}
