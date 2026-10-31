using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace SafeAuthenticator.QR;

/// <summary>
/// Generates QR code images (PNG) from otpauth URIs or arbitrary text.
/// </summary>
public sealed class QrCodeGenerator
{
    /// <summary>
    /// Renders the supplied text as a QR code and returns PNG bytes.
    /// </summary>
    public byte[] GeneratePng(string text, int size = 256)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var writer = new QRCodeWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 1,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
        };

        var matrix = writer.encode(text, BarcodeFormat.QR_CODE, size, size, hints);
        using var bitmap = ToBitmap(matrix);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap ToBitmap(BitMatrix matrix)
    {
        var width = matrix.Width;
        var height = matrix.Height;
        var bitmap = new SKBitmap(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
            }
        }

        return bitmap;
    }
}
