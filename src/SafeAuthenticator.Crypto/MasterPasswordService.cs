// File:    MasterPasswordService.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace SafeAuthenticator.Crypto;

/// <summary>
/// Derives a key-encryption-key from the master password using Argon2id.
/// </summary>
public sealed class MasterPasswordService : IMasterPasswordService
{
    /// <inheritdoc />
    public byte[] DeriveKey(string masterPassword, byte[] salt, Argon2Parameters? parameters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterPassword);
        ArgumentNullException.ThrowIfNull(salt);

        var p = parameters ?? Argon2Parameters.Default;
        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(masterPassword);

        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            MemorySize = p.MemorySizeKib,
            Iterations = p.Iterations,
            DegreeOfParallelism = p.DegreeOfParallelism,
        };

        return argon2.GetBytes(p.KeyLength);
    }

    /// <inheritdoc />
    public byte[] GenerateSalt(int length = 16)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return RandomNumberGenerator.GetBytes(length);
    }
}
