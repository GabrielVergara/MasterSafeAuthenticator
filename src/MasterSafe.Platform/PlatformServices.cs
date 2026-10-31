// File:    PlatformServices.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Crypto;

namespace MasterSafe.Platform;

/// <summary>Creates the platform-specific services available on the current operating system.</summary>
public static class PlatformServices
{
    /// <summary>
    /// Hardware or OS unlock methods for this platform. Windows offers Windows Hello and FIDO2 security keys.
    /// Other systems currently have none; security keys (FIDO2) are added separately.
    /// </summary>
    public static IReadOnlyList<ISecretProvider> CreateUnlockProviders()
    {
#if WINDOWS
        return [new WindowsHelloProvider(), new Fido2HmacSecretProvider()];
#else
        return [];
#endif
    }
}
