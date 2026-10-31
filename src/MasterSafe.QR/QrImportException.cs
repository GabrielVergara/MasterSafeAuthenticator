// File:    QrImportException.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.QR;

/// <summary>
/// Thrown when a QR image cannot be decoded into a valid otpauth account.
/// </summary>
public sealed class QrImportException : Exception
{
    public QrImportException(string message)
        : base(message)
    {
    }

    public QrImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
