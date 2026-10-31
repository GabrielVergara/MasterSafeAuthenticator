// File:    MlKemProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;

namespace MasterSafe.PostQuantum;

/// <summary>
/// ML-KEM-768 (FIPS 203) using the platform cryptography provider through .NET.
/// Available when <see cref="MLKem.IsSupported"/> is true (Windows with post-quantum CNG, or OpenSSL 3.5+).
/// </summary>
public sealed class MlKemProvider : IPostQuantumEncryptionProvider
{
    /// <inheritdoc />
    public string AlgorithmName => "ML-KEM-768";

    /// <inheritdoc />
    public bool IsAvailable => MLKem.IsSupported;

    /// <inheritdoc />
    public PqKeyPair GenerateKeyPair()
    {
        EnsureSupported();

        using var key = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);
        return new PqKeyPair(key.ExportEncapsulationKey(), key.ExportDecapsulationKey());
    }

    /// <inheritdoc />
    public EncapsulationResult Encapsulate(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        EnsureSupported();

        using var key = MLKem.ImportEncapsulationKey(MLKemAlgorithm.MLKem768, publicKey);
        key.Encapsulate(out var ciphertext, out var sharedSecret);
        return new EncapsulationResult(ciphertext, sharedSecret);
    }

    /// <inheritdoc />
    public byte[] Decapsulate(byte[] privateKey, byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(ciphertext);
        EnsureSupported();

        using var key = MLKem.ImportDecapsulationKey(MLKemAlgorithm.MLKem768, privateKey);
        return key.Decapsulate(ciphertext);
    }

    private static void EnsureSupported()
    {
        if (!MLKem.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "ML-KEM is not supported by this operating system's cryptography provider.");
        }
    }
}
