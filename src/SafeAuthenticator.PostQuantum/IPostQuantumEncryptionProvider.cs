namespace SafeAuthenticator.PostQuantum;

/// <summary>
/// Result of a key-encapsulation operation.
/// </summary>
/// <param name="Ciphertext">The encapsulated ciphertext to transmit to the holder of the private key.</param>
/// <param name="SharedSecret">The derived shared secret (symmetric key material).</param>
public sealed record EncapsulationResult(byte[] Ciphertext, byte[] SharedSecret);

/// <summary>
/// A post-quantum KEM key pair.
/// </summary>
/// <param name="PublicKey">Public key bytes.</param>
/// <param name="PrivateKey">Private key bytes.</param>
public sealed record PqKeyPair(byte[] PublicKey, byte[] PrivateKey);

/// <summary>
/// Abstraction over a post-quantum key-encapsulation mechanism (KEM), designed around ML-KEM-768.
/// Concrete implementations must be backed by a vetted cryptographic library — never a mock.
/// </summary>
public interface IPostQuantumEncryptionProvider
{
    /// <summary>Algorithm identifier (e.g. "ML-KEM-768").</summary>
    string AlgorithmName { get; }

    /// <summary>True when a real, production-grade implementation is available on this platform.</summary>
    bool IsAvailable { get; }

    /// <summary>Generates a new KEM key pair.</summary>
    PqKeyPair GenerateKeyPair();

    /// <summary>Encapsulates a shared secret against a recipient public key.</summary>
    EncapsulationResult Encapsulate(byte[] publicKey);

    /// <summary>Decapsulates a shared secret using the recipient private key.</summary>
    byte[] Decapsulate(byte[] privateKey, byte[] ciphertext);
}
