// File:    ISecretProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Crypto;

/// <summary>Result of enrolling an unlock method: data to store in the vault slot and the key it produced.</summary>
/// <param name="Data">Provider-specific enrollment data (for example a challenge). Not secret.</param>
/// <param name="Key">The 32-byte key that wraps the vault data key. The caller must zero it.</param>
public sealed record SecretEnrollment(byte[] Data, byte[] Key);

/// <summary>
/// An unlock method backed by a hardware or operating-system factor, such as Windows Hello
/// or a FIDO2 security key. It turns a user-verified action into a 32-byte key.
/// </summary>
public interface ISecretProvider
{
    /// <summary>The <see cref="SlotTypes"/> value stored in the vault slot.</summary>
    string SlotType { get; }

    /// <summary>Name shown to the user.</summary>
    string DisplayName { get; }

    /// <summary>Short instruction shown while enrolling, for example what the operating system will ask for.</summary>
    string EnrollmentHint => string.Empty;

    /// <summary>True when the factor can be used on this computer right now.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the credential for a new slot and returns the key it produces.</summary>
    Task<SecretEnrollment> EnrollAsync(string slotId, CancellationToken cancellationToken = default);

    /// <summary>Asks the user to verify and returns the same key that enrollment produced.</summary>
    Task<byte[]> DeriveAsync(string slotId, byte[] enrollmentData, CancellationToken cancellationToken = default);

    /// <summary>Deletes the credential created for a slot. Does nothing if it no longer exists.</summary>
    Task RemoveAsync(string slotId, CancellationToken cancellationToken = default);
}
