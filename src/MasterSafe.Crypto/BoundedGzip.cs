// File:    BoundedGzip.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.IO.Compression;

namespace MasterSafe.Crypto;

/// <summary>GZip helpers that refuse to expand data past a fixed limit (decompression-bomb guard).</summary>
public static class BoundedGzip
{
    /// <summary>Largest decompressed size accepted (96 MiB: the attachments allowed in a vault plus its other contents).</summary>
    public const int DefaultMaxBytes = 96 * 1024 * 1024;

    /// <summary>Compresses <paramref name="data"/>.</summary>
    public static byte[] Compress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>Decompresses <paramref name="data"/>, failing if the result would exceed <paramref name="maxBytes"/>.</summary>
    /// <exception cref="InvalidDataException">The data is not valid GZip or expands past the limit.</exception>
    public static byte[] Decompress(byte[] data, int maxBytes = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > maxBytes)
            {
                throw new InvalidDataException("The decompressed data is larger than the allowed limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
