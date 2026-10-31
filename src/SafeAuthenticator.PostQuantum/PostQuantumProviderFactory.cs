// File:    PostQuantumProviderFactory.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

namespace SafeAuthenticator.PostQuantum;

/// <summary>
/// Resolves the best available <see cref="IPostQuantumEncryptionProvider"/> for the current platform.
/// </summary>
public static class PostQuantumProviderFactory
{
    /// <summary>
    /// Returns a usable provider. Today this is the unavailable placeholder; when a real ML-KEM
    /// backend is integrated it will be selected here based on <see cref="IPostQuantumEncryptionProvider.IsAvailable"/>.
    /// </summary>
    public static IPostQuantumEncryptionProvider Create()
    {
        var candidates = new IPostQuantumEncryptionProvider[]
        {
            new UnavailableMlKemProvider(),
        };

        foreach (var candidate in candidates)
        {
            if (candidate.IsAvailable)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }
}
