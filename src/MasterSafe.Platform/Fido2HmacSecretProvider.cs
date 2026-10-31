// File:    Fido2HmacSecretProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

#if WINDOWS
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MasterSafe.Crypto;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Authentication.WebAuthn;

namespace MasterSafe.Platform;

/// <summary>
/// Unlocks the vault with a FIDO2 security key (for example a Thales SafeNet, YubiKey or any key that supports the
/// CTAP2 <c>hmac-secret</c> extension) through the Windows WebAuthn API, the same one browsers use.
/// </summary>
/// <remarks>
/// Enrollment creates a non-resident credential on the key with the hmac-secret extension. The key can then
/// compute HMAC-SHA-256(secret inside the key, salt) only after the user touches it and enters the key's PIN.
/// The credential id and a random salt are stored in the vault slot; neither is secret. The 32-byte result,
/// run through HKDF, wraps the vault's data key. The key's secret never leaves the key, so the vault cannot be
/// opened without the physical key and its PIN. Windows shows its own prompts for the PIN and the touch.
/// </remarks>
public sealed unsafe class Fido2HmacSecretProvider : ISecretProvider
{
    private const string RpId = "mastersafe.local";
    private const string RpName = "MasterSafe Authenticator";
    private const string Origin = "https://mastersafe.local";
    private const uint TimeoutMilliseconds = 90_000;
    private const uint MinimumApiVersion = 6;

    // webauthn.h constants
    private const uint AttachmentCrossPlatform = 2;
    private const uint UserVerificationRequired = 1;
    private const uint AttestationNone = 1;
    private const uint HmacSecretValuesFlag = 0x00100000;
    private const int CoseEs256 = -7;
    private const int SaltLength = 32;

    private static readonly byte[] KdfInfo = Encoding.UTF8.GetBytes("MasterSafe.Fido2.v1");

    /// <inheritdoc />
    public string SlotType => SlotTypes.Fido2;

    /// <inheritdoc />
    public string DisplayName => "Security key";

    /// <inheritdoc />
    public string EnrollmentHint
        => "Keep the key plugged in. Windows will ask for the key's PIN and a touch, twice. If the key has no PIN yet, Windows helps you set one.";

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(PInvoke.WebAuthNGetApiVersionNumber() >= MinimumApiVersion);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<SecretEnrollment> EnrollAsync(string slotId, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var credentialId = CreateCredential();
            var secret = GetHmacSecret(credentialId, salt);
            try
            {
                return new SecretEnrollment(Pack(credentialId, salt), DeriveKey(secret, slotId));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> DeriveAsync(string slotId, byte[] enrollmentData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrollmentData);

        return Task.Run(() =>
        {
            var (credentialId, salt) = Unpack(enrollmentData);
            var secret = GetHmacSecret(credentialId, salt);
            try
            {
                return DeriveKey(secret, slotId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string slotId, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // The credential is not stored on the key, so there is nothing to delete.

    private static byte[] DeriveKey(byte[] secret, string slotId)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, Encoding.UTF8.GetBytes(slotId), KdfInfo);

    private static byte[] CreateCredential()
    {
        var apiVersion = ApiVersion();
        using var native = new NativeScope();

        var rp = new WEBAUTHN_RP_ENTITY_INFORMATION
        {
            dwVersion = 1,
            pwszId = native.Text(RpId),
            pwszName = native.Text(RpName),
        };

        var user = new WEBAUTHN_USER_ENTITY_INFORMATION
        {
            dwVersion = 1,
            cbId = 16,
            pbId = native.Bytes(RandomNumberGenerator.GetBytes(16)),
            pwszName = native.Text("MasterSafe vault"),
            pwszDisplayName = native.Text("MasterSafe vault"),
        };

        var parameter = native.Alloc<WEBAUTHN_COSE_CREDENTIAL_PARAMETER>();
        *parameter = new WEBAUTHN_COSE_CREDENTIAL_PARAMETER
        {
            dwVersion = 1,
            pwszCredentialType = native.Text("public-key"),
            lAlg = CoseEs256,
        };
        var parameters = new WEBAUTHN_COSE_CREDENTIAL_PARAMETERS { cCredentialParameters = 1, pCredentialParameters = parameter };

        var clientData = ClientData(native, "webauthn.create");

        // Ask the key to enable the hmac-secret extension for this credential.
        var enabled = native.Alloc<int>();
        *enabled = 1;
        var extension = native.Alloc<WEBAUTHN_EXTENSION>();
        *extension = new WEBAUTHN_EXTENSION
        {
            pwszExtensionIdentifier = native.Text("hmac-secret"),
            cbExtension = sizeof(int),
            pvExtension = enabled,
        };

        var options = new WEBAUTHN_AUTHENTICATOR_MAKE_CREDENTIAL_OPTIONS
        {
            dwVersion = Math.Min(apiVersion, 9u),
            dwTimeoutMilliseconds = TimeoutMilliseconds,
            Extensions = new WEBAUTHN_EXTENSIONS { cExtensions = 1, pExtensions = extension },
            dwAuthenticatorAttachment = AttachmentCrossPlatform,
            bRequireResidentKey = false,
            dwUserVerificationRequirement = UserVerificationRequired,
            dwAttestationConveyancePreference = AttestationNone,
        };

        WEBAUTHN_CREDENTIAL_ATTESTATION* attestation = null;
        var hr = PInvoke.WebAuthNAuthenticatorMakeCredential(Window(), &rp, &user, &parameters, &clientData, &options, &attestation);
        if (hr.Failed)
        {
            throw Fail(hr);
        }

        try
        {
            if (!HmacSecretEnabled(attestation))
            {
                throw new InvalidOperationException(
                    "This security key does not support the hmac-secret extension that MasterSafe needs. Try another key.");
            }

            return new ReadOnlySpan<byte>(attestation->pbCredentialId, (int)attestation->cbCredentialId).ToArray();
        }
        finally
        {
            PInvoke.WebAuthNFreeCredentialAttestation(attestation);
        }
    }

    private static bool HmacSecretEnabled(WEBAUTHN_CREDENTIAL_ATTESTATION* attestation)
    {
        if (attestation->bPrfEnabled)
        {
            return true;
        }

        for (var i = 0; i < attestation->Extensions.cExtensions; i++)
        {
            var extension = attestation->Extensions.pExtensions[i];
            if (extension.pwszExtensionIdentifier.ToString() == "hmac-secret"
                && extension.cbExtension >= sizeof(int)
                && *(int*)extension.pvExtension != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] GetHmacSecret(byte[] credentialId, byte[] salt)
    {
        var apiVersion = ApiVersion();
        using var native = new NativeScope();

        var clientData = ClientData(native, "webauthn.get");

        var credential = native.Alloc<WEBAUTHN_CREDENTIAL_EX>();
        *credential = new WEBAUTHN_CREDENTIAL_EX
        {
            dwVersion = 1,
            cbId = (uint)credentialId.Length,
            pbId = native.Bytes(credentialId),
            pwszCredentialType = native.Text("public-key"),
            dwTransports = 0,
        };
        var credentialPointer = native.Alloc<nint>();
        *credentialPointer = (nint)credential;
        var allowList = new WEBAUTHN_CREDENTIAL_LIST { cCredentials = 1, ppCredentials = (WEBAUTHN_CREDENTIAL_EX**)credentialPointer };

        var saltStruct = native.Alloc<WEBAUTHN_HMAC_SECRET_SALT>();
        *saltStruct = new WEBAUTHN_HMAC_SECRET_SALT { cbFirst = (uint)salt.Length, pbFirst = native.Bytes(salt) };
        var saltValues = new WEBAUTHN_HMAC_SECRET_SALT_VALUES { pGlobalHmacSalt = saltStruct };

        var options = new WEBAUTHN_AUTHENTICATOR_GET_ASSERTION_OPTIONS
        {
            dwVersion = Math.Min(apiVersion, 9u),
            dwTimeoutMilliseconds = TimeoutMilliseconds,
            dwAuthenticatorAttachment = AttachmentCrossPlatform,
            dwUserVerificationRequirement = UserVerificationRequired,
            dwFlags = HmacSecretValuesFlag,
            pAllowCredentialList = &allowList,
            pHmacSecretSaltValues = &saltValues,
        };

        WEBAUTHN_ASSERTION* assertion = null;
        var hr = PInvoke.WebAuthNAuthenticatorGetAssertion(Window(), native.Text(RpId), &clientData, &options, &assertion);
        if (hr.Failed)
        {
            throw Fail(hr);
        }

        try
        {
            if (assertion->pHmacSecret is null || assertion->pHmacSecret->cbFirst != SaltLength)
            {
                throw new InvalidOperationException("The security key did not return a secret. It may not support hmac-secret.");
            }

            return new ReadOnlySpan<byte>(assertion->pHmacSecret->pbFirst, SaltLength).ToArray();
        }
        finally
        {
            PInvoke.WebAuthNFreeAssertion(assertion);
        }
    }

    private static WEBAUTHN_CLIENT_DATA ClientData(NativeScope native, string type)
    {
        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var json = Encoding.UTF8.GetBytes($"{{\"type\":\"{type}\",\"challenge\":\"{challenge}\",\"origin\":\"{Origin}\",\"crossOrigin\":false}}");
        return new WEBAUTHN_CLIENT_DATA
        {
            dwVersion = 1,
            cbClientDataJSON = (uint)json.Length,
            pbClientDataJSON = native.Bytes(json),
            pwszHashAlgId = native.Text("SHA-256"),
        };
    }

    private static uint ApiVersion()
    {
        var version = PInvoke.WebAuthNGetApiVersionNumber();
        if (version < MinimumApiVersion)
        {
            throw new PlatformNotSupportedException("This version of Windows is too old to use a security key with MasterSafe. Update Windows 11.");
        }

        return version;
    }

    private static HWND Window()
    {
        var foreground = PInvoke.GetForegroundWindow();
        return foreground.IsNull ? PInvoke.GetDesktopWindow() : foreground;
    }

    private static Exception Fail(HRESULT hr)
    {
        // Cancel, timeout and "no matching key" all come back as NotAllowedError.
        var name = PInvoke.WebAuthNGetErrorName(hr).ToString();
        return name == "NotAllowedError"
            ? new OperationCanceledException("The security key was not found, or the request was cancelled or timed out.")
            : new InvalidOperationException($"The security key request failed ({name}, 0x{(uint)hr.Value:X8}).");
    }

    // Layout: 2-byte big-endian credential id length, credential id, salt.
    private static byte[] Pack(byte[] credentialId, byte[] salt)
    {
        var data = new byte[2 + credentialId.Length + salt.Length];
        data[0] = (byte)(credentialId.Length >> 8);
        data[1] = (byte)credentialId.Length;
        credentialId.CopyTo(data, 2);
        salt.CopyTo(data, 2 + credentialId.Length);
        return data;
    }

    private static (byte[] CredentialId, byte[] Salt) Unpack(byte[] data)
    {
        if (data.Length < 2 + SaltLength)
        {
            throw new InvalidDataException("The security key data in the vault is damaged.");
        }

        var idLength = (data[0] << 8) | data[1];
        if (data.Length != 2 + idLength + SaltLength || idLength == 0)
        {
            throw new InvalidDataException("The security key data in the vault is damaged.");
        }

        return (data.AsSpan(2, idLength).ToArray(), data.AsSpan(2 + idLength, SaltLength).ToArray());
    }

    /// <summary>Owns unmanaged memory handed to Windows for the duration of one call and frees it afterwards.</summary>
    private sealed class NativeScope : IDisposable
    {
        private readonly List<nint> _allocations = [];

        public PCWSTR Text(string value)
        {
            var pointer = Marshal.StringToHGlobalUni(value);
            _allocations.Add(pointer);
            return new PCWSTR((char*)pointer);
        }

        public byte* Bytes(byte[] value)
        {
            var pointer = Marshal.AllocHGlobal(Math.Max(1, value.Length));
            _allocations.Add(pointer);
            Marshal.Copy(value, 0, pointer, value.Length);
            return (byte*)pointer;
        }

        public T* Alloc<T>()
            where T : unmanaged
        {
            var pointer = Marshal.AllocHGlobal(sizeof(T));
            new Span<byte>((void*)pointer, sizeof(T)).Clear();
            _allocations.Add(pointer);
            return (T*)pointer;
        }

        public void Dispose()
        {
            foreach (var pointer in _allocations)
            {
                Marshal.FreeHGlobal(pointer);
            }

            _allocations.Clear();
        }
    }
}
#endif
