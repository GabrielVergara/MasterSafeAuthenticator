using SafeAuthenticator.Core;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace SafeAuthenticator.QR;

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

        using var bitmap = SKBitmap.Decode(imageStream)
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
