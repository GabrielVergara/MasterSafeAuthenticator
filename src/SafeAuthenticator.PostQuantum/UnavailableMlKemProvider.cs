// File:    UnavailableMlKemProvider.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

namespace SafeAuthenticator.PostQuantum;

/// <summary>
/// Placeholder provider that advertises ML-KEM-768 but is deliberately NOT available.
/// <para>
/// .NET exposes ML-KEM via <c>System.Security.Cryptography.MLKem</c> on platforms where the
/// underlying OpenSSL 3.5+/Windows CNG support exists. When that support is confirmed, replace this
/// with a real implementation. This class intentionally throws instead of faking PQC so callers
/// cannot accidentally rely on insecure placeholder output.
/// </para>
/// </summary>
public sealed class UnavailableMlKemProvider : IPostQuantumEncryptionProvider
{
    /// <inheritdoc />
    public string AlgorithmName => "ML-KEM-768";

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public PqKeyPair GenerateKeyPair() => throw CreateException();

    /// <inheritdoc />
    public EncapsulationResult Encapsulate(byte[] publicKey) => throw CreateException();

    /// <inheritdoc />
    public byte[] Decapsulate(byte[] privateKey, byte[] ciphertext) => throw CreateException();

    private static NotSupportedException CreateException() => new(
        "ML-KEM-768 is not available in this build. A real implementation must be wired in " +
        "via System.Security.Cryptography.MLKem or a vetted library before post-quantum " +
        "protection can be used. See docs/PQC.md.");
}
