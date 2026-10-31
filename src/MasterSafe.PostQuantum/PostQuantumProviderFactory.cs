// File:    PostQuantumProviderFactory.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.PostQuantum;

/// <summary>Chooses a post-quantum provider for the current platform.</summary>
public static class PostQuantumProviderFactory
{
    /// <summary>
    /// Returns native ML-KEM-768 when the operating system supports it. Otherwise it returns a
    /// provider that reports <see cref="IPostQuantumEncryptionProvider.IsAvailable"/> as false and throws,
    /// rather than returning insecure output.
    /// </summary>
    public static IPostQuantumEncryptionProvider Create()
        => new MlKemProvider().IsAvailable ? new MlKemProvider() : new UnavailableMlKemProvider();

    /// <summary>
    /// Returns the hybrid ECDH-P384 + ML-KEM-768 provider when supported. Use this one for new
    /// encryption, because it stays secure if either algorithm is later broken.
    /// </summary>
    public static IPostQuantumEncryptionProvider CreateHybrid()
        => new HybridKemProvider().IsAvailable ? new HybridKemProvider() : new UnavailableMlKemProvider();
}
