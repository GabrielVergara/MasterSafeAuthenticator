// File:    EmergencySheet.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Net;
using System.Text;

namespace MasterSafe.App.Services;

/// <summary>What goes on the printed emergency sheet. No secrets are in here: those are written on the paper by hand.</summary>
/// <param name="VaultPath">Where the vault is.</param>
/// <param name="RecoveryKeyId">Identifier of the recovery key, or null.</param>
/// <param name="BackupFolder">The folder of automatic recovery backups, or null.</param>
/// <param name="SyncPlace">The sync folder or server, or null.</param>
/// <param name="Printed">The date of the sheet.</param>
public sealed record EmergencySheetData(string VaultPath, string? RecoveryKeyId, string? BackupFolder, string? SyncPlace, DateTimeOffset Printed);

/// <summary>
/// Builds a large-print page to keep on paper. It works with nothing but a browser: no scripts, no links, nothing loaded
/// from anywhere. It says where things are, with empty boxes to fill in by hand, because anything secret typed into a
/// file would then sit on the disk unprotected.
/// </summary>
public static class EmergencySheet
{
    /// <summary>The page as HTML, in the current language.</summary>
    public static string BuildHtml(EmergencySheetData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        static string H(string text) => WebUtility.HtmlEncode(text);
        static string T(string text) => H(Loc.T(text));

        var page = new StringBuilder();
        page.Append("<!doctype html>\n<html lang=\"").Append(Loc.Current).Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(T("MasterSafe emergency sheet")).Append("</title>\n<style>\n")
            .Append("body{font:18pt/1.5 Arial,Helvetica,sans-serif;color:#000;background:#fff;margin:2cm;max-width:17cm}\n")
            .Append("h1{font-size:26pt;margin:0 0 .3em}h2{font-size:20pt;margin:1em 0 .2em}\n")
            .Append(".box{border:2px solid #000;height:1.8em;margin:.2em 0 .8em}.value{font-family:monospace;word-break:break-all}\n")
            .Append("</style></head><body>\n");

        page.Append("<h1>").Append(T("MasterSafe emergency sheet")).Append("</h1>\n");
        page.Append("<p>").Append(T("Keep this sheet somewhere safe, away from your computer. Fill it in by hand. Anyone who has this sheet filled in, together with your files, can open your vault.")).Append("</p>\n");
        page.Append("<p>").Append(T("Date")).Append(": ").Append(H(data.Printed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append("</p>\n");

        page.Append("<h2>").Append(T("Write these by hand")).Append("</h2>\n");
        foreach (var label in new[] { "Master password", "Recovery key passphrase", "Sync code" })
        {
            page.Append("<p>").Append(T(label)).Append("</p><div class=\"box\"></div>\n");
        }

        page.Append("<h2>").Append(T("Where things are")).Append("</h2>\n");
        Line(page, T("Vault file"), data.VaultPath);
        Line(page, T("Recovery key"), data.RecoveryKeyId);
        Line(page, T("Recovery backups"), data.BackupFolder);
        Line(page, T("Sync"), data.SyncPlace);
        page.Append("<p>").Append(T("Where the recovery key file is kept")).Append("</p><div class=\"box\"></div>\n");

        page.Append("<h2>").Append(T("If you lost your computer or your master password")).Append("</h2>\n<ol>\n");
        foreach (var step in new[]
        {
            "Install MasterSafe Authenticator from the project's GitHub release page.",
            "Choose Forgot your master password? on the sign-in screen.",
            "Choose the recovery key file, type its passphrase, and choose a recovery backup.",
            "Set a new master password. Your data comes back as it was when the backup was made.",
        })
        {
            page.Append("<li>").Append(T(step)).Append("</li>\n");
        }

        page.Append("</ol>\n</body></html>\n");
        return page.ToString();
    }

    private static void Line(StringBuilder page, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            page.Append("<p>").Append(label).Append(": <span class=\"value\">").Append(WebUtility.HtmlEncode(value)).Append("</span></p>\n");
        }
    }
}
