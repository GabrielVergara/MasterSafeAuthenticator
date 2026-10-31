// File:    QrLimitsTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using FluentAssertions;
using MasterSafe.QR;
using Xunit;

namespace MasterSafe.Tests;

public class QrLimitsTests
{
    [Fact]
    public void OversizedImageFile_IsRefusedBeforeDecoding()
    {
        var importer = new QrCodeImporter();
        using var stream = new MemoryStream(new byte[QrCodeImporter.MaxImageBytes + 1]);

        var act = () => importer.DecodeText(stream);

        act.Should().Throw<QrImportException>().WithMessage("*too large*");
    }

    [Fact]
    public void HugePicture_IsRefusedBeforeItIsDecodedIntoMemory()
    {
        // A tiny PNG whose header claims 30000 x 30000 pixels (900 million). Decoding it would need gigabytes.
        var importer = new QrCodeImporter();
        using var stream = new MemoryStream(PngHeaderOnly(30000, 30000));

        var act = () => importer.DecodeText(stream);

        act.Should().Throw<QrImportException>();
    }

    [Fact]
    public void NotAnImage_IsRejectedCleanly()
    {
        var importer = new QrCodeImporter();
        using var stream = new MemoryStream("definitely not an image"u8.ToArray());

        var act = () => importer.DecodeText(stream);

        act.Should().Throw<QrImportException>();
    }

    private static byte[] PngHeaderOnly(uint width, uint height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: RGB

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(output, "IHDR"u8.ToArray(), ihdr);
        WriteChunk(output, "IEND"u8.ToArray(), []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, byte[] type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. type, .. data]));
        output.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
