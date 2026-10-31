// File:    QrCodeImporter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Core;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace MasterSafe.QR;

/// <summary>
/// Supported image formats for QR import.
/// </summary>
public static class QrImageFormats
{
    /// <summary>File extensions (lower-case, with dot) accepted for QR import.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>Returns true when the extension is a supported image type.</summary>
    public static bool IsSupported(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }
}

/// <summary>
/// Decodes otpauth QR codes from images using ZXing.Net and ImageSharp.
/// </summary>
public sealed class QrCodeImporter
{

    /// <summary>Largest image file that will be read (20 MiB).</summary>
    public const long MaxImageBytes = 20L * 1024 * 1024;

    /// <summary>Largest picture that will be decoded (40 million pixels).</summary>
    public const long MaxPixels = 40_000_000;

    private readonly BarcodeReaderGeneric _reader;

    public QrCodeImporter()
    {
        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
    }

    /// <summary>
    /// Decodes the raw text payload of a QR code from an image file.
    /// </summary>
    public string? DecodeText(string imagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(imagePath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file not found.", imagePath);
        }

        if (!QrImageFormats.IsSupported(imagePath))
        {
            throw new NotSupportedException($"Unsupported image format: {Path.GetExtension(imagePath)}");
        }

        using var stream = File.OpenRead(imagePath);
        return DecodeText(stream);
    }

    /// <summary>
    /// Decodes the raw text payload of a QR code from an image stream.
    /// </summary>
    public string? DecodeText(Stream imageStream)
    {
        ArgumentNullException.ThrowIfNull(imageStream);

        // Images come from files the user picked or dropped, so they are treated as untrusted: the size of the file
        // and of the picture are limited before the picture is decoded into memory.
        using var buffered = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = imageStream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffered.Length + read > MaxImageBytes)
            {
                throw new QrImportException("The image file is too large.");
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        using var codec = SKCodec.Create(buffered)
            ?? throw new QrImportException("The image could not be decoded.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            throw new QrImportException("The image is too large (more than 40 million pixels).");
        }

        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new QrImportException("The image could not be decoded.");
        var source = ToLuminanceSource(bitmap);
        var result = _reader.Decode(source);
        return result?.Text;
    }

    /// <summary>
    /// Imports an <see cref="OtpAccount"/> from a QR image file. Rejects non-otpauth / malformed codes.
    /// </summary>
    public OtpAccount ImportFromFile(string imagePath)
    {
        var text = DecodeText(imagePath)
            ?? throw new QrImportException("No QR code was found in the image.");
        return ParsePayload(text);
    }

    /// <summary>
    /// Imports an <see cref="OtpAccount"/> from a QR image stream (e.g. clipboard/drag-drop).
    /// </summary>
    public OtpAccount ImportFromStream(Stream imageStream)
    {
        var text = DecodeText(imageStream)
            ?? throw new QrImportException("No QR code was found in the image.");
        return ParsePayload(text);
    }

    /// <summary>Attempts a QR import without throwing.</summary>
    public bool TryImportFromFile(string imagePath, out OtpAccount? account)
    {
        try
        {
            account = ImportFromFile(imagePath);
            return true;
        }
        catch (Exception ex) when (ex is QrImportException or System.FormatException or NotSupportedException or FileNotFoundException)
        {
            account = null;
            return false;
        }
    }

    private static OtpAccount ParsePayload(string text)
    {
        if (!OtpAuthParser.TryParse(text, out var account) || account is null)
        {
            throw new QrImportException("QR code does not contain a valid otpauth:// URI.");
        }

        return account;
    }

    private static RGBLuminanceSource ToLuminanceSource(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rgbBytes = new byte[width * height * 3];
        var index = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                rgbBytes[index++] = pixel.Red;
                rgbBytes[index++] = pixel.Green;
                rgbBytes[index++] = pixel.Blue;
            }
        }

        return new RGBLuminanceSource(rgbBytes, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
    }
}
