namespace SafeAuthenticator.Core;

/// <summary>
/// Hash algorithms supported for OTP generation (RFC 6238 / RFC 4226).
/// </summary>
public enum OtpAlgorithm
{
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>
/// One-time password type.
/// </summary>
public enum OtpType
{
    Totp,
    Hotp,
}
