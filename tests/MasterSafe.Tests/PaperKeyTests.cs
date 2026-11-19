// File:    PaperKeyTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.Backup;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;
using MasterSafe.QR;
using SkiaSharp;
using Xunit;

namespace MasterSafe.Tests;

public sealed class PaperKeyTests
{
    private const string Passphrase = "a long recovery passphrase";

    private static readonly Argon2Parameters FastKdf = new() { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };

    private static RecoveryKeyService NewKeys()
        => new(new AesGcmEncryptionService(), new MasterPasswordService(), PostQuantumProviderFactory.CreateHybrid(), FastKdf);

    [Fact]
    public void Split_ThenJoin_GivesAKeyThatOpensWithItsPassphrase_InAFewPieces()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var created = keys.Create(Passphrase);
        var pieces = PaperKey.Split(created.FileBytes);

        pieces.Should().HaveCountLessThanOrEqualTo(4);
        pieces.Should().OnlyContain(p => p.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c) || c == '/'));

        var back = PaperKey.Join(pieces);
        var fromPaper = keys.OpenPrivateKey(back, Passphrase);
        var fromFile = keys.OpenPrivateKey(created.FileBytes, Passphrase);
        fromPaper.Should().Equal(fromFile);
        keys.ReadPublicInfo(back).KeyId.Should().Be(created.KeyId);
    }

    [Fact]
    public void Join_AcceptsTypedText_InAnyOrder_WithSpacesLowercaseAndRepeats()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var created = NewKeys().Create(Passphrase);
        var pieces = PaperKey.Split(created.FileBytes);
        var typed = pieces.Reverse().Select(p => RecoveryKeySheet.FormatPiece(p).ToLowerInvariant()).ToList();

        PaperKey.Join([.. typed, pieces[0]]).Should().Equal(PaperKey.Join(pieces));
    }

    [Fact]
    public void Join_SaysWhichPieceHasATypo_AndWhichAreMissing()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var pieces = PaperKey.Split(NewKeys().Create(Passphrase).FileBytes).ToList();
        pieces.Count.Should().BeGreaterThan(1);

        var last = pieces[1][^1];
        var typo = pieces[1][..^1] + (last == 'A' ? 'B' : 'A');
        FluentActions.Invoking(() => PaperKey.Join([pieces[0], typo, .. pieces.Skip(2)]))
            .Should().Throw<InvalidDataException>().WithMessage("Piece 2 has a mistake*");

        FluentActions.Invoking(() => PaperKey.Join(pieces.Skip(1)))
            .Should().Throw<InvalidDataException>().WithMessage($"Missing pieces: 1 of {pieces.Count}.");
    }

    [Fact]
    public void Join_RefusesPiecesOfTwoKeys_AndText_ThatIsNotAKey()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var a = PaperKey.Split(keys.Create(Passphrase).FileBytes);
        var b = PaperKey.Split(keys.Create(Passphrase).FileBytes);

        FluentActions.Invoking(() => PaperKey.Join([a[0], .. b.Skip(1)]))
            .Should().Throw<InvalidDataException>().WithMessage("*different recovery keys*");
        FluentActions.Invoking(() => PaperKey.Join(["hello"])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => PaperKey.Join(["MSRK1/1/1/AAAA/AAAA/ABC"])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => PaperKey.Join([new string('A', 100_000)])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => PaperKey.Split("{\"Note\":\"x\"}"u8.ToArray())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ThePrintedPage_HoldsNoScriptsOrLinks_AndOnePhotoOfItsCodesReadsBackTheKey()
    {
        if (!MLKem.IsSupported)
        {
            return;
        }

        var keys = NewKeys();
        var created = keys.Create(Passphrase);
        var pieces = PaperKey.Split(created.FileBytes);

        var html = RecoveryKeySheet.BuildHtml(created.FileBytes, created.KeyId, DateTimeOffset.Now);
        html.Should().NotContainAny("<script", "http:", "https:", "src=\"/", Passphrase);
        html.Split("data:image/png;base64,").Should().HaveCount(pieces.Count + 1);

        // All the codes on one "photo", each scaled up as a printer would, with a gap between them.
        var qr = new QrCodeGenerator();
        var images = pieces.Select(p => SKBitmap.Decode(qr.GeneratePrintPng(p))).ToList();
        const int scale = 5;
        var side = images.Max(i => i.Width) * scale;
        using var page = new SKBitmap((side * 2) + 40, ((images.Count + 1) / 2) * (side + 40), SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            for (var i = 0; i < images.Count; i++)
            {
                using var image = SKImage.FromBitmap(images[i]);
                canvas.DrawImage(image, SKRect.Create((i % 2) * (side + 40), (i / 2) * (side + 40), side, side), new SKSamplingOptions(SKFilterMode.Nearest));
            }
        }

        var file = Path.Combine(Path.GetTempPath(), "ms-paper-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var data = SKImage.FromBitmap(page).Encode(SKEncodedImageFormat.Png, 100))
            {
                File.WriteAllBytes(file, data.ToArray());
            }

            var read = new QrCodeImporter().DecodeAllTexts(file);
            read.Should().BeEquivalentTo(pieces);
            keys.OpenPrivateKey(PaperKey.Join(read), Passphrase).Should().Equal(keys.OpenPrivateKey(created.FileBytes, Passphrase));
        }
        finally
        {
            File.Delete(file);
            images.ForEach(i => i.Dispose());
        }
    }
}
