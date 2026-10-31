// File:    ReleaseSigner.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;

namespace MasterSafe.PostQuantum;

/// <summary>
/// Signs and verifies release files (installers, checksums, SBOMs) with ML-DSA-65 (FIPS 204), a
/// post-quantum signature. The public key is published with the project; the private seed never leaves
/// the maintainer. Verifying a download against that public key shows it was not altered.
/// </summary>
public static class ReleaseSigner
{
    /// <summary>True when the operating system provides ML-DSA.</summary>
    public static bool IsSupported => MLDsa.IsSupported;

    /// <summary>Creates a signing key pair. The seed (32 bytes) is the private key.</summary>
    public static (byte[] PublicKey, byte[] PrivateSeed) GenerateKeyPair()
    {
        EnsureSupported();

        using var key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        return (key.ExportMLDsaPublicKey(), key.ExportMLDsaPrivateSeed());
    }

    /// <summary>Signs <paramref name="data"/> with the private seed.</summary>
    public static byte[] Sign(byte[] data, byte[] privateSeed)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(privateSeed);
        EnsureSupported();

        using var key = MLDsa.ImportMLDsaPrivateSeed(MLDsaAlgorithm.MLDsa65, privateSeed);
        return key.SignData(data);
    }

    /// <summary>Returns true when <paramref name="signature"/> is a valid signature of <paramref name="data"/>.</summary>
    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);
        EnsureSupported();

        try
        {
            using var key = MLDsa.ImportMLDsaPublicKey(MLDsaAlgorithm.MLDsa65, publicKey);
            return key.VerifyData(data, signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static void EnsureSupported()
    {
        if (!MLDsa.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "ML-DSA is not supported by this operating system's cryptography provider.");
        }
    }
}
