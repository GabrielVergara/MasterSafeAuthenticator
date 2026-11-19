// File:    RecoveryKeySheet.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Net;
using System.Text;
using MasterSafe.Backup;
using MasterSafe.QR;

namespace MasterSafe.App.Services;

/// <summary>
/// Builds the recovery key on paper: a page with large-print instructions and each piece of the key as a QR code and as
/// text. Like the emergency sheet it needs nothing but a browser: no scripts, no links, the codes are inside the page.
/// The key in it is still locked with its passphrase, which is never put on the page.
/// </summary>
public static class RecoveryKeySheet
{
    /// <summary>Characters per printed line of a piece, in groups of <see cref="Group"/>.</summary>
    public const int LineLength = 60;

    private const int Group = 5;

    /// <summary>The page as HTML, in the current language.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a recovery key file.</exception>
    public static string BuildHtml(byte[] keyFile, string keyId, DateTimeOffset printed)
    {
        ArgumentNullException.ThrowIfNull(keyFile);

        static string H(string text) => WebUtility.HtmlEncode(text);
        static string T(string text) => H(Loc.T(text));

        var pieces = PaperKey.Split(keyFile);
        var qr = new QrCodeGenerator();

        var page = new StringBuilder();
        page.Append("<!doctype html>\n<html lang=\"").Append(Loc.Current).Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(T("MasterSafe recovery key on paper")).Append("</title>\n<style>\n")
            .Append("body{font:18pt/1.5 Arial,Helvetica,sans-serif;color:#000;background:#fff;margin:1.5cm;max-width:18cm}\n")
            .Append("h1{font-size:26pt;margin:0 0 .3em}h2{font-size:20pt;margin:.6em 0 .2em}\n")
            .Append(".codes{display:flex;flex-wrap:wrap;gap:.5cm;break-before:page;page-break-before:always}\n")
            .Append(".piece{break-inside:avoid;page-break-inside:avoid;font-size:14pt}\n")
            .Append(".piece img{width:8.5cm;height:8.5cm;image-rendering:pixelated;image-rendering:crisp-edges;display:block}\n")
            .Append(".typed{break-before:page;page-break-before:always}\n")
            .Append(".text{font:10pt/1.5 'Courier New',monospace;white-space:pre;margin:.2em 0 .6cm;break-inside:avoid;page-break-inside:avoid}\n")
            .Append(".id{font-family:monospace}\n")
            .Append("</style></head><body>\n");

        page.Append("<h1>").Append(T("MasterSafe recovery key on paper")).Append("</h1>\n");
        page.Append("<p>").Append(T("This is your recovery key, printed. It is locked with the recovery key passphrase, which is not on this sheet. Keep the sheet and the passphrase in different places.")).Append("</p>\n");
        page.Append("<p>").Append(T("Key id")).Append(": <span class=\"id\">").Append(H(keyId)).Append("</span><br>")
            .Append(T("Date")).Append(": ").Append(H(printed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append("</p>\n");

        page.Append("<h2>").Append(T("How to use it")).Append("</h2>\n<ol>\n");
        foreach (var step in new[]
        {
            "On the sign-in screen, choose Forgot your master password? and then Restore from a recovery backup.",
            "Choose Read a printed key... and pick a photo or scan of each page of this sheet.",
            "If a code cannot be read, type its text from the last pages in the box below. Spaces and line breaks do not matter.",
            "Type the recovery key passphrase, choose the recovery backup and set a new master password.",
        })
        {
            page.Append("<li>").Append(T(step)).Append("</li>\n");
        }

        page.Append("</ol>\n<div class=\"codes\">\n");
        for (var i = 0; i < pieces.Count; i++)
        {
            page.Append("<div class=\"piece\">").Append(H(Loc.T($"Piece {i + 1} of {pieces.Count}")))
                .Append("<img alt=\"").Append(H(Loc.T($"QR code, piece {i + 1}"))).Append("\" src=\"data:image/png;base64,")
                .Append(Convert.ToBase64String(qr.GeneratePrintPng(pieces[i]))).Append("\"></div>\n");
        }

        page.Append("</div>\n<div class=\"typed\"><h2>").Append(T("The same pieces as text")).Append("</h2>\n<p>")
            .Append(T("Only needed if a code above cannot be read. Type the piece that is missing, starting with MSRK1.")).Append("</p>\n");
        foreach (var piece in pieces)
        {
            page.Append("<div class=\"text\">").Append(H(FormatPiece(piece))).Append("</div>\n");
        }

        page.Append("</div>\n</body></html>\n");
        return page.ToString();
    }

    /// <summary>A piece as it is printed: its heading on one line, then the data in short groups, easy to type.</summary>
    public static string FormatPiece(string piece)
    {
        ArgumentException.ThrowIfNullOrEmpty(piece);

        var split = piece.LastIndexOf('/') + 1;
        var text = new StringBuilder(piece[..split]).Append('\n');
        for (var line = split; line < piece.Length; line += LineLength)
        {
            var row = piece.Substring(line, Math.Min(LineLength, piece.Length - line));
            text.Append(string.Join(' ', row.Chunk(Group).Select(g => new string(g)))).Append('\n');
        }

        return text.ToString();
    }
}
