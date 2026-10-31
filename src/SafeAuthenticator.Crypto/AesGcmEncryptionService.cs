// File:    AesGcmEncryptionService.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Security.Cryptography;

namespace SafeAuthenticator.Crypto;

/// <summary>
/// AES-256-GCM authenticated encryption service.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <inheritdoc />
    public EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        ValidateKey(key);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedPayload(nonce, ciphertext, tag);
    }

    /// <inheritdoc />
    public byte[] Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKey(key);

        var plaintext = new byte[payload.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new InvalidPasswordException("Authentication failed: wrong key or tampered data.", ex);
        }

        return plaintext;
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be {KeySize} bytes (AES-256).", nameof(key));
        }
    }
}
