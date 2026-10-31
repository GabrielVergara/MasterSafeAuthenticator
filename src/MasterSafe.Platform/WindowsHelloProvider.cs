// File:    WindowsHelloProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

#if WINDOWS
using System.Security.Cryptography;
using System.Text;
using MasterSafe.Crypto;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace MasterSafe.Platform;

/// <summary>
/// Unlocks the vault with Windows Hello (PIN, fingerprint or face). Windows creates a private key
/// that stays in the TPM or Windows' protected store and signs a stored challenge only after the
/// user verifies. An RSA PKCS#1 signature of a fixed challenge is always the same, so the signature
/// is turned into the 32-byte key that wraps the vault's data key. Nothing that can open the
/// vault is stored on disk, and the key cannot be used without the Hello prompt.
/// </summary>
public sealed class WindowsHelloProvider : ISecretProvider
{
    private static readonly byte[] KdfInfo = Encoding.UTF8.GetBytes("MasterSafe.WindowsHello.v1");

    /// <inheritdoc />
    public string SlotType => SlotTypes.WindowsHello;

    /// <inheritdoc />
    public string DisplayName => "Windows Hello";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<SecretEnrollment> EnrollAsync(string slotId, CancellationToken cancellationToken = default)
    {
        var created = await KeyCredentialManager.RequestCreateAsync(CredentialName(slotId), KeyCredentialCreationOption.ReplaceExisting);
        EnsureSuccess(created.Status);

        var challenge = RandomNumberGenerator.GetBytes(32);
        var key = await DeriveKeyAsync(created.Credential, challenge, slotId);
        return new SecretEnrollment(challenge, key);
    }

    /// <inheritdoc />
    public async Task<byte[]> DeriveAsync(string slotId, byte[] enrollmentData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrollmentData);

        var opened = await KeyCredentialManager.OpenAsync(CredentialName(slotId));
        EnsureSuccess(opened.Status);
        return await DeriveKeyAsync(opened.Credential, enrollmentData, slotId);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string slotId, CancellationToken cancellationToken = default)
    {
        try
        {
            await KeyCredentialManager.DeleteAsync(CredentialName(slotId));
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    private static string CredentialName(string slotId) => "MasterSafe." + slotId;

    private static async Task<byte[]> DeriveKeyAsync(KeyCredential credential, byte[] challenge, string slotId)
    {
        var signed = await credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
        EnsureSuccess(signed.Status);

        CryptographicBuffer.CopyToByteArray(signed.Result, out var signature);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, 32, Encoding.UTF8.GetBytes(slotId), KdfInfo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static void EnsureSuccess(KeyCredentialStatus status)
    {
        switch (status)
        {
            case KeyCredentialStatus.Success:
                return;
            case KeyCredentialStatus.UserCanceled:
                throw new OperationCanceledException("Windows Hello was cancelled.");
            case KeyCredentialStatus.NotFound:
                throw new InvalidOperationException("The Windows Hello credential for this vault no longer exists. Use your master password and set it up again.");
            case KeyCredentialStatus.UserPrefersPassword:
                throw new InvalidOperationException("Windows Hello is not set up for this account.");
            default:
                throw new InvalidOperationException($"Windows Hello is not available ({status}).");
        }
    }
}
#endif
