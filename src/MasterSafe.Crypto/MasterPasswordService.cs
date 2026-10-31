// File:    MasterPasswordService.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace MasterSafe.Crypto;

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
        p.Validate();

        if (salt.Length < 8)
        {
            throw new InvalidDataException("The key-derivation salt is too short.");
        }

        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(masterPassword);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = p.MemorySizeKib,
                Iterations = p.Iterations,
                DegreeOfParallelism = p.DegreeOfParallelism,
            };

            return argon2.GetBytes(p.KeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <inheritdoc />
    public byte[] GenerateSalt(int length = 32)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return RandomNumberGenerator.GetBytes(length);
    }
}
