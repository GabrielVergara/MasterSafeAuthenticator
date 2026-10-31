// File:    KeePassImporter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MasterSafe.Core.Import;

/// <summary>
/// Reads the unencrypted exports of KeePass 2.x (KeePass XML) and KeePassXC (CSV or XML). The files are untrusted, so
/// DTDs and external entities are off and the size, entry count and nesting depth are limited.
/// </summary>
public static class KeePassImporter
{
    /// <summary>Largest export that is read.</summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    /// <summary>Most entries read from one file.</summary>
    public const int MaxEntries = 50_000;

    private const int MaxGroupDepth = 64;
    private const int MaxFieldLength = 1_000_000;

    /// <summary>Reads an export from disk, choosing XML or CSV from the content.</summary>
    public static KeePassImportResult ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new KeePassImportException("The file does not exist.");
        }

        if (info.Length > MaxFileBytes)
        {
            throw new KeePassImportException($"The file is larger than {MaxFileBytes / (1024 * 1024)} MiB, which is more than MasterSafe reads.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KeePassImportException($"The file could not be read: {ex.Message}");
        }

        if (bytes.Length >= 4 && bytes[0] == 0x03 && bytes[1] == 0xD9 && bytes[2] == 0xA2 && bytes[3] == 0x9A)
        {
            throw new KeePassImportException(
                "This is a KeePass database (.kdbx). Opening it directly is not supported yet. In KeePass choose File, Export, KeePass XML (2.x), "
                + "or in KeePassXC choose Database, Export to CSV, and import that file.");
        }

        var text = DecodeText(bytes);
        return text.TrimStart().StartsWith('<') ? ParseXml(text) : ParseCsv(text);
    }

    /// <summary>Parses a KeePass XML (2.x) export.</summary>
    public static KeePassImportResult ParseXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxFileBytes * 2,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new KeePassImportException($"The file is not a valid KeePass XML export: {ex.Message}");
        }

        if (document.Root?.Name.LocalName != "KeePassFile")
        {
            throw new KeePassImportException("The file is not a KeePass XML export (no KeePassFile element).");
        }

        var result = new KeePassImportResult();
        var recycleBin = document.Root.Element("Meta")?.Element("RecycleBinUUID")?.Value;
        var root = document.Root.Element("Root")?.Element("Group");
        if (root is null)
        {
            throw new KeePassImportException("The export contains no groups.");
        }

        var protectedValues = false;
        ReadGroup(root, [], isRoot: true, recycleBin, result, ref protectedValues, depth: 0);
        if (protectedValues)
        {
            result.Warnings.Add("Some values are protected inside the file and could not be read. Export again with KeePass's own export command.");
        }

        return result;
    }

    /// <summary>Parses a CSV export from KeePassXC or KeePass 2.x.</summary>
    public static KeePassImportResult ParseCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var rows = CsvRows(csv);
        if (rows.Count == 0)
        {
            throw new KeePassImportException("The file is empty.");
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        var title = Col("title", "account");
        var user = Col("username", "user name", "login name", "login");
        var password = Col("password");
        var url = Col("url", "web site", "website");
        var notes = Col("notes", "comments", "note");
        var group = Col("group", "group tree", "grouping");
        var totp = Col("totp", "otp");

        if (title < 0 || password < 0)
        {
            throw new KeePassImportException("The file has no Title and Password columns, so it does not look like a KeePass or KeePassXC export.");
        }

        var result = new KeePassImportResult();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 1 && row[0].Length == 0)
            {
                continue;
            }

            if (++result.TotalEntries > MaxEntries)
            {
                throw new KeePassImportException($"The file has more than {MaxEntries} entries.");
            }

            string Get(int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;
            var path = Get(group).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (path.Count > 0 && path[0].Equals("Root", StringComparison.OrdinalIgnoreCase))
            {
                path.RemoveAt(0);
            }

            AddEntry(result, Get(title), Get(user), Get(password), Get(url), Get(notes), Get(totp), path, DateTimeOffset.UtcNow);
        }

        return result;
    }

    private static void ReadGroup(
        XElement group, List<string> path, bool isRoot, string? recycleBin, KeePassImportResult result, ref bool protectedValues, int depth)
    {
        if (depth > MaxGroupDepth)
        {
            throw new KeePassImportException("The groups are nested too deeply.");
        }

        var name = group.Element("Name")?.Value ?? string.Empty;
        var here = isRoot ? path : [.. path, name];
        var inRecycleBin = !string.IsNullOrEmpty(recycleBin) && group.Element("UUID")?.Value == recycleBin;

        foreach (var entry in group.Elements("Entry"))
        {
            if (++result.TotalEntries > MaxEntries)
            {
                throw new KeePassImportException($"The file has more than {MaxEntries} entries.");
            }

            if (inRecycleBin)
            {
                result.SkippedRecycleBin++;
                continue;
            }

            result.SkippedHistory += entry.Element("History")?.Elements("Entry").Count() ?? 0;
            result.SkippedAttachments += entry.Elements("Binary").Count();

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in entry.Elements("String"))
            {
                var key = field.Element("Key")?.Value;
                var value = field.Element("Value");
                if (key is null || value is null)
                {
                    continue;
                }

                if (string.Equals((string?)value.Attribute("Protected"), "True", StringComparison.OrdinalIgnoreCase))
                {
                    protectedValues = true;
                    continue;
                }

                if (value.Value.Length > MaxFieldLength)
                {
                    throw new KeePassImportException($"A field in '{fields.GetValueOrDefault("Title")}' is too long.");
                }

                fields[key] = value.Value;
            }

            var modified = DateTimeOffset.TryParse(
                entry.Element("Times")?.Element("LastModificationTime")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            AddEntry(
                result,
                fields.GetValueOrDefault("Title", string.Empty),
                fields.GetValueOrDefault("UserName", string.Empty),
                fields.GetValueOrDefault("Password", string.Empty),
                fields.GetValueOrDefault("URL", string.Empty),
                fields.GetValueOrDefault("Notes", string.Empty),
                OtpValue(fields),
                here,
                modified);
        }

        foreach (var child in group.Elements("Group"))
        {
            ReadGroup(child, here, isRoot: false, recycleBin, result, ref protectedValues, depth + 1);
        }
    }

    /// <summary>Collects the one-time-password settings of an entry into an otpauth link or a key string.</summary>
    private static string OtpValue(Dictionary<string, string> fields)
    {
        if (fields.TryGetValue("otp", out var otp) && !string.IsNullOrWhiteSpace(otp))
        {
            return otp;
        }

        if (!fields.TryGetValue("TimeOtp-Secret-Base32", out var secret) || string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        var algorithm = fields.GetValueOrDefault("TimeOtp-Algorithm", string.Empty).ToUpperInvariant() switch
        {
            "HMAC-SHA-256" => "SHA256",
            "HMAC-SHA-512" => "SHA512",
            _ => "SHA1",
        };

        return $"key={secret}&period={fields.GetValueOrDefault("TimeOtp-Period", "30")}"
            + $"&digits={fields.GetValueOrDefault("TimeOtp-Length", "6")}&algorithm={algorithm}";
    }

    private static void AddEntry(
        KeePassImportResult result,
        string title,
        string user,
        string password,
        string url,
        string notes,
        string otp,
        List<string> groupPath,
        DateTimeOffset modified)
    {
        var folder = FolderPath.Normalize(string.Join(FolderPath.Separator, groupPath));
        IReadOnlyList<string> tags = folder.Length == 0 ? [] : [folder];
        var added = false;

        if (!string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(title))
        {
            result.Passwords.Add(new PasswordEntry
            {
                Title = title.Trim(),
                UserName = user.Trim(),
                Password = password,
                Url = url.Trim(),
                Notes = notes,
                Folder = folder,
                ModifiedUtc = modified,
            });
            added = true;
        }

        if (!string.IsNullOrWhiteSpace(otp))
        {
            var account = ToAccount(otp, title, user, tags);
            if (account is not null)
            {
                result.Accounts.Add(account);
                added = true;
            }
            else
            {
                result.Warnings.Add($"The one-time password of '{title}' was not understood and was skipped.");
            }
        }

        if (!added)
        {
            result.SkippedInvalid++;
        }
    }

    private static OtpAccount? ToAccount(string otp, string title, string user, IReadOnlyList<string> tags)
    {
        otp = otp.Trim();
        var fallbackLabel = string.IsNullOrWhiteSpace(user) ? title.Trim() : user.Trim();
        OtpAccount account;

        if (otp.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (!OtpAuthParser.TryParse(otp, out var parsed) || parsed is null)
            {
                return null;
            }

            account = new OtpAccount
            {
                Issuer = string.IsNullOrWhiteSpace(parsed.Issuer) ? title.Trim() : parsed.Issuer,
                Label = string.IsNullOrWhiteSpace(parsed.Label) ? fallbackLabel : parsed.Label,
                Secret = parsed.Secret,
                Type = parsed.Type,
                Algorithm = parsed.Algorithm,
                Digits = parsed.Digits,
                Period = parsed.Period,
                Counter = parsed.Counter,
                Tags = tags,
            };
        }
        else
        {
            var query = otp.Contains('=', StringComparison.Ordinal)
                ? otp.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .GroupBy(p => p[0].Trim().ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = otp };

            if (!query.TryGetValue("key", out var secret) && !query.TryGetValue("secret", out secret))
            {
                return null;
            }

            account = new OtpAccount
            {
                Issuer = title.Trim(),
                Label = fallbackLabel,
                Secret = secret,
                Algorithm = query.GetValueOrDefault("algorithm", "SHA1").ToUpperInvariant() switch
                {
                    "SHA256" => OtpAlgorithm.Sha256,
                    "SHA512" => OtpAlgorithm.Sha512,
                    _ => OtpAlgorithm.Sha1,
                },
                Digits = int.TryParse(query.GetValueOrDefault("digits", "6"), CultureInfo.InvariantCulture, out var digits) ? digits : 6,
                Period = int.TryParse(query.GetValueOrDefault("period", "30"), CultureInfo.InvariantCulture, out var period) ? period : 30,
                Tags = tags,
            };
        }

        var normalised = new OtpAccount
        {
            Issuer = account.Issuer,
            Label = account.Label,
            Secret = account.Secret.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            Type = account.Type,
            Algorithm = account.Algorithm,
            Digits = account.Digits,
            Period = account.Period,
            Counter = account.Counter,
            Tags = account.Tags,
        };

        return normalised.IsValid(out _) ? normalised : null;
    }

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Splits RFC 4180 CSV into rows; quoted fields may contain commas, quotes and line breaks.</summary>
    private static List<List<string>> CsvRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else if (c != '\r')
                {
                    // Line breaks inside a note are kept as "\n" whatever the file used.
                    field.Append(c);
                }

                if (field.Length > MaxFieldLength)
                {
                    throw new KeePassImportException("A field in the file is too long.");
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    if (rows.Count > MaxEntries + 1)
                    {
                        throw new KeePassImportException($"The file has more than {MaxEntries} entries.");
                    }

                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
