// File:    ReleaseSigner.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace MasterSafe.PostQuantum;

/// <summary>
/// Signs and verifies release files (installers, checksums, SBOMs) with ML-DSA-65 (FIPS 204), a
/// post-quantum signature. The public key is published with the project; the private seed never leaves
/// the maintainer. Verifying a download against that public key shows it was not altered.
/// </summary>
public static class ReleaseSigner
{
    /// <summary>True when the operating system provides ML-DSA, which signing needs.</summary>
    public static bool IsSupported => MLDsa.IsSupported;

    /// <summary>
    /// Always true: verifying also works without the operating system's ML-DSA (for example OpenSSL 3.0 on Ubuntu 24.04 and
    /// Linux Mint 22), through the managed BouncyCastle implementation.
    /// </summary>
    public static bool CanVerify => true;

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
        if (!MLDsa.IsSupported)
        {
            return VerifyManaged(data, signature, publicKey);
        }

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

    /// <summary>
    /// Verifies with BouncyCastle's managed ML-DSA-65 (pure mode, empty context, as <see cref="MLDsa.SignData(byte[], byte[])"/>
    /// signs). Used when the operating system has no ML-DSA. Public for tests, which compare it with the system one.
    /// </summary>
    public static bool VerifyManaged(byte[] data, byte[] signature, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.Length != MLDsaAlgorithm.MLDsa65.PublicKeySizeInBytes || signature.Length != MLDsaAlgorithm.MLDsa65.SignatureSizeInBytes)
        {
            return false;
        }

        try
        {
            var signer = new MLDsaSigner(MLDsaParameters.ml_dsa_65, deterministic: true);
            signer.Init(false, MLDsaPublicKeyParameters.FromEncoding(MLDsaParameters.ml_dsa_65, publicKey));
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Org.BouncyCastle.Crypto.CryptoException)
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
