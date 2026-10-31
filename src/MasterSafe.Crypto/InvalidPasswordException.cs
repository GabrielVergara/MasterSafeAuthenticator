// File:    InvalidPasswordException.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Crypto;

/// <summary>
/// Thrown when a master password fails to decrypt the vault.
/// </summary>
public sealed class InvalidPasswordException : Exception
{
    public InvalidPasswordException()
        : base("The master password is incorrect or the vault is corrupted.")
    {
    }

    public InvalidPasswordException(string message)
        : base(message)
    {
    }

    public InvalidPasswordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
