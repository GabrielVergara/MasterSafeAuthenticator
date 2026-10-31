// File:    HybridKemProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;

namespace MasterSafe.PostQuantum;

/// <summary>
/// Hybrid key encapsulation: ECDH on NIST P-384 combined with ML-KEM-768.
/// The shared secret stays safe as long as either algorithm is unbroken, so it is never weaker than
/// today's classical baseline and is also safe against a future quantum computer. Both secrets and the
/// full transcript (both public keys and both ciphertexts) go through HKDF-SHA-384.
/// </summary>
public sealed class HybridKemProvider : IPostQuantumEncryptionProvider
{
    private static readonly byte[] Label = Encoding.UTF8.GetBytes("MasterSafe.HybridKEM.v1");

    /// <inheritdoc />
    public string AlgorithmName => "ECDH-P384+ML-KEM-768";

    /// <inheritdoc />
    public bool IsAvailable => MLKem.IsSupported;

    /// <inheritdoc />
    public PqKeyPair GenerateKeyPair()
    {
        EnsureSupported();

        using var ec = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        using var ml = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);

        var ecPublic = ec.ExportSubjectPublicKeyInfo();
        var ecPrivate = ec.ExportPkcs8PrivateKey();
        var mlPublic = ml.ExportEncapsulationKey();
        var mlPrivate = ml.ExportDecapsulationKey();
        try
        {
            return new PqKeyPair(Pack(ecPublic, mlPublic), Pack(ecPrivate, mlPrivate));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ecPrivate);
            CryptographicOperations.ZeroMemory(mlPrivate);
        }
    }

    /// <inheritdoc />
    public EncapsulationResult Encapsulate(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        EnsureSupported();

        var (ecPublic, mlPublic) = Unpack(publicKey);

        using var recipientEc = ECDiffieHellman.Create();
        recipientEc.ImportSubjectPublicKeyInfo(ecPublic, out _);
        if (recipientEc.KeySize != 384)
        {
            throw new CryptographicException("The recipient key is not a P-384 key.");
        }

        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var ephemeralPublic = ephemeral.ExportSubjectPublicKeyInfo();
        var ecSecret = ephemeral.DeriveRawSecretAgreement(recipientEc.PublicKey);

        using var mlKey = MLKem.ImportEncapsulationKey(MLKemAlgorithm.MLKem768, mlPublic);
        mlKey.Encapsulate(out var mlCiphertext, out var mlSecret);

        try
        {
            var shared = Combine(ecSecret, mlSecret, ecPublic, mlPublic, ephemeralPublic, mlCiphertext);
            return new EncapsulationResult(Pack(ephemeralPublic, mlCiphertext), shared);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ecSecret);
            CryptographicOperations.ZeroMemory(mlSecret);
        }
    }

    /// <inheritdoc />
    public byte[] Decapsulate(byte[] privateKey, byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(ciphertext);
        EnsureSupported();

        var (ecPrivate, mlPrivate) = Unpack(privateKey);
        var (ephemeralPublic, mlCiphertext) = Unpack(ciphertext);

        using var ec = ECDiffieHellman.Create();
        ec.ImportPkcs8PrivateKey(ecPrivate, out _);
        using var ephemeral = ECDiffieHellman.Create();
        ephemeral.ImportSubjectPublicKeyInfo(ephemeralPublic, out _);
        if (ephemeral.KeySize != 384)
        {
            throw new CryptographicException("The ciphertext does not contain a P-384 key.");
        }

        var ecSecret = ec.DeriveRawSecretAgreement(ephemeral.PublicKey);

        using var ml = MLKem.ImportDecapsulationKey(MLKemAlgorithm.MLKem768, mlPrivate);
        var mlSecret = ml.Decapsulate(mlCiphertext);
        var ecPublic = ec.ExportSubjectPublicKeyInfo();
        var mlPublic = ml.ExportEncapsulationKey();

        try
        {
            return Combine(ecSecret, mlSecret, ecPublic, mlPublic, ephemeralPublic, mlCiphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ecSecret);
            CryptographicOperations.ZeroMemory(mlSecret);
            CryptographicOperations.ZeroMemory(ecPrivate);
            CryptographicOperations.ZeroMemory(mlPrivate);
        }
    }

    private static byte[] Combine(
        byte[] ecSecret, byte[] mlSecret, byte[] ecPublic, byte[] mlPublic, byte[] ephemeralPublic, byte[] mlCiphertext)
    {
        var ikm = new byte[ecSecret.Length + mlSecret.Length];
        ecSecret.CopyTo(ikm, 0);
        mlSecret.CopyTo(ikm, ecSecret.Length);

        // The transcript is bound in as HKDF info so a ciphertext cannot be replayed to another recipient.
        var info = Pack(Label, Pack(ecPublic, mlPublic), Pack(ephemeralPublic, mlCiphertext));
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA384, ikm, 32, salt: null, info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }

    // Length-prefixed concatenation: 4-byte big-endian length, then the bytes, for each part.
    private static byte[] Pack(params byte[][] parts)
    {
        using var stream = new MemoryStream();
        var length = new byte[4];
        foreach (var part in parts)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
            stream.Write(length);
            stream.Write(part);
        }

        return stream.ToArray();
    }

    private static (byte[] First, byte[] Second) Unpack(byte[] data)
    {
        var offset = 0;
        var first = ReadPart(data, ref offset);
        var second = ReadPart(data, ref offset);
        if (offset != data.Length)
        {
            throw new CryptographicException("The key or ciphertext has unexpected extra data.");
        }

        return (first, second);
    }

    private static byte[] ReadPart(byte[] data, ref int offset)
    {
        if (data.Length - offset < 4)
        {
            throw new CryptographicException("The key or ciphertext is truncated.");
        }

        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
        offset += 4;
        if (length < 0 || length > data.Length - offset)
        {
            throw new CryptographicException("The key or ciphertext is malformed.");
        }

        var part = data.AsSpan(offset, length).ToArray();
        offset += length;
        return part;
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
