// File:    AppExportImporter.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace MasterSafe.Core.Import;

/// <summary>
/// Reads the unencrypted exports of other apps: Bitwarden, 1Password and Proton Pass (passwords), and Aegis, 2FAS and plain
/// lists of otpauth links (authenticator codes). The files are untrusted, so sizes and counts are limited, and a zip file is read
/// only up to the limit however large it says it is. An encrypted export cannot be read here, and the message says so.
/// </summary>
public static class AppExportImporter
{
    /// <summary>Reads the export inside a zip file (1Password .1pux or Proton Pass .zip). Throws when there is none.</summary>
    public static string ReadZip(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("export.data", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries.FirstOrDefault(e => e.Name.Equals("data.json", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                ?? throw new KeePassImportException("This zip file does not hold an export that MasterSafe knows.");

            using var input = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > KeePassImporter.MaxFileBytes)
                {
                    throw new KeePassImportException("The export inside the zip file is too large.");
                }

                output.Write(buffer, 0, read);
            }

            return System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            throw new KeePassImportException($"The zip file could not be read: {ex.Message}");
        }
    }

    /// <summary>Reads a JSON export, recognising the app from its shape.</summary>
    public static KeePassImportResult ParseJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new KeePassImportException("This file is not an export that MasterSafe knows.");
            }

            if (root.TryGetProperty("accounts", out var onePassword) && onePassword.ValueKind == JsonValueKind.Array)
            {
                return Read1Password(onePassword);
            }

            if (root.TryGetProperty("services", out _) || root.TryGetProperty("servicesEncrypted", out _))
            {
                return Read2Fas(root);
            }

            if (root.TryGetProperty("db", out var db))
            {
                return ReadAegis(root, db);
            }

            if (root.TryGetProperty("vaults", out var vaults) && vaults.ValueKind == JsonValueKind.Object)
            {
                return ReadProton(root, vaults);
            }

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return ReadBitwarden(root, items);
            }

            throw new KeePassImportException("This file is not an export that MasterSafe knows.");
        }
        catch (JsonException ex)
        {
            throw new KeePassImportException($"The export is damaged: {ex.Message}");
        }
    }

    /// <summary>Reads a text file of otpauth links, one per line (Ente Auth and others).</summary>
    public static KeePassImportResult ParseOtpLinks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new KeePassImportResult();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(KeePassImporter.MaxEntries))
        {
            result.TotalEntries++;
            if (OtpAuthParser.TryParse(line, out var account) && account is not null)
            {
                result.Accounts.Add(account);
            }
            else if (line.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase) && GoogleMigration.TryParse(line, out var accounts))
            {
                result.Accounts.AddRange(accounts);
            }
            else
            {
                result.SkippedInvalid++;
            }
        }

        return result;
    }

    private static string Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty,
            }
            : string.Empty;

    private static JsonElement Child(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static IEnumerable<JsonElement> List(JsonElement element, string name)
        => Child(element, name) is { ValueKind: JsonValueKind.Array } list ? list.EnumerateArray() : [];

    private static void Field(List<CustomField> fields, string name, string value, bool secret = false)
    {
        if (value.Length > 0 && fields.Count < CustomField.MaxPerEntry)
        {
            fields.Add(new CustomField { Name = name, Value = value, IsSecret = secret });
        }
    }

    private static void Count(KeePassImportResult result)
    {
        if (++result.TotalEntries > KeePassImporter.MaxEntries)
        {
            throw new KeePassImportException($"The file has more than {KeePassImporter.MaxEntries} entries.");
        }
    }

    private static DateTimeOffset FromUnix(JsonElement item, string name)
        => Child(item, name) is { ValueKind: JsonValueKind.Number } time && time.TryGetInt64(out var seconds) && seconds is > 0 and < 32503680000
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;

    // ------------------------------------------------------------------ Bitwarden

    private static KeePassImportResult ReadBitwarden(JsonElement root, JsonElement items)
    {
        if (Child(root, "encrypted") is { ValueKind: JsonValueKind.True })
        {
            throw new KeePassImportException("This Bitwarden export is encrypted. Export again as .json without the password protection, then import that file.");
        }

        var folders = List(root, "folders").ToDictionary(f => Text(f, "id"), f => Text(f, "name"), StringComparer.Ordinal);
        var result = new KeePassImportResult();
        foreach (var item in items.EnumerateArray())
        {
            Count(result);
            var login = Child(item, "login");
            var fields = new List<CustomField>();
            foreach (var field in List(item, "fields"))
            {
                Field(fields, Text(field, "name"), Text(field, "value"), Text(field, "type") == "1");
            }

            var type = Text(item, "type");
            if (type == "3")
            {
                var card = Child(item, "card");
                Field(fields, "Cardholder name", Text(card, "cardholderName"));
                Field(fields, "Brand", Text(card, "brand"));
                Field(fields, "Card number", Text(card, "number"), secret: true);
                Field(fields, "Expiry", $"{Text(card, "expMonth")}/{Text(card, "expYear")}".Trim('/'));
                Field(fields, "Security code", Text(card, "code"), secret: true);
            }
            else if (type == "4")
            {
                var identity = Child(item, "identity");
                Field(fields, "Full name", string.Join(' ', new[] { Text(identity, "firstName"), Text(identity, "middleName"), Text(identity, "lastName") }.Where(n => n.Length > 0)));
                Field(fields, "Email", Text(identity, "email"));
                Field(fields, "Phone", Text(identity, "phone"));
                Field(fields, "Address", string.Join(", ", new[] { Text(identity, "address1"), Text(identity, "city"), Text(identity, "postalCode"), Text(identity, "country") }.Where(n => n.Length > 0)));
                Field(fields, "Passport or ID number", Text(identity, "passportNumber"), secret: true);
            }

            var kind = type switch
            {
                "2" => EntryKind.Note,
                "3" => EntryKind.Card,
                "4" => EntryKind.Identity,
                _ => EntryKind.Login,
            };

            KeePassImporter.AddItem(
                result,
                new PasswordEntry
                {
                    Title = Text(item, "name"),
                    UserName = Text(login, "username"),
                    Password = Text(login, "password"),
                    Url = List(login, "uris").Select(u => Text(u, "uri")).FirstOrDefault() ?? string.Empty,
                    Notes = Text(item, "notes"),
                    Folder = FolderPath.Normalize(folders.GetValueOrDefault(Text(item, "folderId"), string.Empty)),
                    Kind = kind,
                    Fields = fields,
                    ModifiedUtc = DateTimeOffset.TryParse(Text(item, "revisionDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : DateTimeOffset.UtcNow,
                },
                Text(login, "totp"));
        }

        return result;
    }

    // ------------------------------------------------------------------ 1Password

    private static KeePassImportResult Read1Password(JsonElement accounts)
    {
        var result = new KeePassImportResult();
        foreach (var account in accounts.EnumerateArray())
        {
            foreach (var vault in List(account, "vaults"))
            {
                var vaultName = Text(Child(vault, "attrs"), "name");
                foreach (var item in List(vault, "items"))
                {
                    Count(result);
                    if (Text(item, "state") == "archived")
                    {
                        result.SkippedRecycleBin++;
                        continue;
                    }

                    Read1PasswordItem(item, vaultName, result);
                }
            }
        }

        return result;
    }

    private static void Read1PasswordItem(JsonElement item, string vaultName, KeePassImportResult result)
    {
        var overview = Child(item, "overview");
        var details = Child(item, "details");
        var user = string.Empty;
        var password = Text(details, "password");
        foreach (var login in List(details, "loginFields"))
        {
            switch (Text(login, "designation"))
            {
                case "username":
                    user = Text(login, "value");
                    break;
                case "password":
                    password = Text(login, "value");
                    break;
            }
        }

        var fields = new List<CustomField>();
        var otp = string.Empty;
        foreach (var section in List(details, "sections"))
        {
            foreach (var field in List(section, "fields"))
            {
                var value = Child(field, "value");
                if (Text(value, "totp") is { Length: > 0 } totp)
                {
                    otp = totp;
                    continue;
                }

                if (value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var part in value.EnumerateObject())
                {
                    var text = part.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? part.Value.ToString() : string.Empty;
                    Field(fields, Text(field, "title"), text, part.Name is "concealed" or "creditCardNumber");
                }
            }
        }

        KeePassImporter.AddItem(
            result,
            new PasswordEntry
            {
                Title = Text(overview, "title"),
                UserName = user,
                Password = password,
                Url = Text(overview, "url"),
                Notes = Text(details, "notesPlain"),
                Folder = FolderPath.Normalize(vaultName),
                Tags = [.. List(overview, "tags").Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString() ?? string.Empty)],
                Kind = Text(item, "categoryUuid") switch
                {
                    "002" => EntryKind.Card,
                    "003" => EntryKind.Note,
                    "004" => EntryKind.Identity,
                    _ => password.Length == 0 && fields.Count > 0 ? EntryKind.Note : EntryKind.Login,
                },
                Fields = fields,
                ModifiedUtc = FromUnix(item, "updatedAt"),
            },
            otp);
    }

    // ------------------------------------------------------------------ Proton Pass

    private static KeePassImportResult ReadProton(JsonElement root, JsonElement vaults)
    {
        if (Child(root, "encrypted") is { ValueKind: JsonValueKind.True })
        {
            throw new KeePassImportException("This Proton Pass export is encrypted. Export again without PGP encryption, then import that file.");
        }

        var result = new KeePassImportResult();
        foreach (var vault in vaults.EnumerateObject())
        {
            var vaultName = Text(vault.Value, "name");
            foreach (var item in List(vault.Value, "items"))
            {
                Count(result);
                if (Text(item, "state") == "2")
                {
                    result.SkippedRecycleBin++;
                    continue;
                }

                var data = Child(item, "data");
                var metadata = Child(data, "metadata");
                var content = Child(data, "content");
                var fields = new List<CustomField>();
                foreach (var extra in List(data, "extraFields"))
                {
                    Field(fields, Text(extra, "fieldName"), Text(Child(extra, "data"), "content"), Text(extra, "type") == "hidden");
                }

                var type = Text(data, "type");
                if (type == "creditCard")
                {
                    Field(fields, "Cardholder name", Text(content, "cardholderName"));
                    Field(fields, "Card number", Text(content, "number"), secret: true);
                    Field(fields, "Expiry", Text(content, "expirationDate"));
                    Field(fields, "Security code", Text(content, "verificationCode"), secret: true);
                    Field(fields, "PIN", Text(content, "pin"), secret: true);
                }
                else if (type == "identity")
                {
                    Field(fields, "Full name", Text(content, "fullName"));
                    Field(fields, "Email", Text(content, "email"));
                    Field(fields, "Phone", Text(content, "phoneNumber"));
                }

                var user = new[] { Text(content, "itemUsername"), Text(content, "itemEmail"), Text(content, "username"), Text(item, "aliasEmail") }
                    .FirstOrDefault(u => u.Length > 0) ?? string.Empty;

                KeePassImporter.AddItem(
                    result,
                    new PasswordEntry
                    {
                        Title = Text(metadata, "name"),
                        UserName = user,
                        Password = Text(content, "password"),
                        Url = List(content, "urls").Where(u => u.ValueKind == JsonValueKind.String).Select(u => u.GetString() ?? string.Empty).FirstOrDefault() ?? string.Empty,
                        Notes = Text(metadata, "note"),
                        Folder = FolderPath.Normalize(vaultName),
                        Kind = type switch
                        {
                            "note" => EntryKind.Note,
                            "creditCard" => EntryKind.Card,
                            "identity" => EntryKind.Identity,
                            _ => Text(content, "password").Length == 0 ? EntryKind.Note : EntryKind.Login,
                        },
                        Fields = fields,
                        ModifiedUtc = FromUnix(item, "modifyTime"),
                    },
                    Text(content, "totpUri"));
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ Aegis

    private static KeePassImportResult ReadAegis(JsonElement root, JsonElement db)
    {
        if (db.ValueKind == JsonValueKind.String || Child(Child(root, "header"), "slots").ValueKind == JsonValueKind.Array)
        {
            throw new KeePassImportException("This Aegis export is encrypted. Export again and turn off encryption, then import that file.");
        }

        var groups = List(db, "groups").ToDictionary(g => Text(g, "uuid"), g => Text(g, "name"), StringComparer.Ordinal);
        var result = new KeePassImportResult();
        foreach (var entry in List(db, "entries"))
        {
            Count(result);
            var info = Child(entry, "info");
            var groupId = List(entry, "groups").Where(g => g.ValueKind == JsonValueKind.String).Select(g => g.GetString() ?? string.Empty).FirstOrDefault() ?? string.Empty;
            var account = BuildAccount(
                Text(entry, "issuer"),
                Text(entry, "name"),
                Text(info, "secret"),
                Text(entry, "type"),
                Text(info, "algo"),
                Text(info, "digits"),
                Text(info, "period"),
                Text(info, "counter"),
                groups.GetValueOrDefault(groupId, Text(entry, "group")));
            AddAccount(result, account, Text(entry, "name"));
        }

        return result;
    }

    // ------------------------------------------------------------------ 2FAS

    private static KeePassImportResult Read2Fas(JsonElement root)
    {
        if (Text(root, "servicesEncrypted").Length > 0)
        {
            throw new KeePassImportException("This 2FAS export is encrypted. Export again without a password, then import that file.");
        }

        var groups = List(root, "groups").ToDictionary(g => Text(g, "id"), g => Text(g, "name"), StringComparer.Ordinal);
        var result = new KeePassImportResult();
        foreach (var service in List(root, "services"))
        {
            Count(result);
            var otp = Child(service, "otp");
            var issuer = Text(otp, "issuer");
            var account = BuildAccount(
                issuer.Length > 0 ? issuer : Text(service, "name"),
                Text(otp, "account") is { Length: > 0 } name ? name : Text(otp, "label"),
                Text(service, "secret"),
                Text(otp, "tokenType"),
                Text(otp, "algorithm"),
                Text(otp, "digits"),
                Text(otp, "period"),
                Text(otp, "counter"),
                groups.GetValueOrDefault(Text(service, "groupId"), string.Empty));
            AddAccount(result, account, Text(service, "name"));
        }

        return result;
    }

    private static OtpAccount? BuildAccount(
        string issuer, string label, string secret, string type, string algorithm, string digits, string period, string counter, string group)
    {
        if (type.Equals("steam", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var account = new OtpAccount
        {
            Issuer = issuer,
            Label = label,
            Group = group,
            Secret = secret.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
            Type = type.Equals("hotp", StringComparison.OrdinalIgnoreCase) ? OtpType.Hotp : OtpType.Totp,
            Algorithm = algorithm.ToUpperInvariant() switch
            {
                "SHA256" => OtpAlgorithm.Sha256,
                "SHA512" => OtpAlgorithm.Sha512,
                _ => OtpAlgorithm.Sha1,
            },
            Digits = int.TryParse(digits, CultureInfo.InvariantCulture, out var d) ? d : 6,
            Period = int.TryParse(period, CultureInfo.InvariantCulture, out var p) ? p : 30,
            Counter = long.TryParse(counter, CultureInfo.InvariantCulture, out var c) ? c : 0,
        };

        return account.IsValid(out _) ? account : null;
    }

    private static void AddAccount(KeePassImportResult result, OtpAccount? account, string name)
    {
        if (account is null)
        {
            result.SkippedInvalid++;
            result.Warnings.Add($"The account '{name}' could not be used (it may be a type MasterSafe does not support, such as Steam).");
        }
        else
        {
            result.Accounts.Add(account);
        }
    }
}

/// <summary>Reads the <c>otpauth-migration://</c> links of the Google Authenticator export QR codes.</summary>
public static class GoogleMigration
{
    private const int MaxAccounts = 1000;

    /// <summary>Reads a link. False when it is damaged or holds no accounts.</summary>
    public static bool TryParse(string uri, out List<OtpAccount> accounts)
    {
        accounts = [];
        ArgumentNullException.ThrowIfNull(uri);

        const string prefix = "otpauth-migration://offline?data=";
        if (uri.Length > 1_000_000 || !uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var data = Convert.FromBase64String(Pad(Uri.UnescapeDataString(uri[prefix.Length..].Split('&')[0]).Replace('-', '+').Replace('_', '/').Replace(' ', '+')));
            var position = 0;
            while (position < data.Length)
            {
                var (field, wire) = ReadTag(data, ref position);
                if (field == 1 && wire == 2)
                {
                    var length = ReadLength(data, ref position);
                    var account = ReadAccount(data.AsSpan(position, length));
                    if (account is not null && accounts.Count < MaxAccounts)
                    {
                        accounts.Add(account);
                    }

                    position += length;
                }
                else
                {
                    Skip(data, ref position, wire);
                }
            }

            return accounts.Count > 0;
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
        {
            accounts = [];
            return false;
        }
    }

    private static string Pad(string base64) => base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

    private static OtpAccount? ReadAccount(ReadOnlySpan<byte> data)
    {
        var buffer = data.ToArray();
        var position = 0;
        byte[] secret = [];
        var name = string.Empty;
        var issuer = string.Empty;
        long algorithm = 1, digits = 1, type = 2, counter = 0;

        while (position < buffer.Length)
        {
            var (field, wire) = ReadTag(buffer, ref position);
            if (wire == 2)
            {
                var length = ReadLength(buffer, ref position);
                var value = buffer.AsSpan(position, length);
                switch (field)
                {
                    case 1:
                        secret = value.ToArray();
                        break;
                    case 2:
                        name = System.Text.Encoding.UTF8.GetString(value);
                        break;
                    case 3:
                        issuer = System.Text.Encoding.UTF8.GetString(value);
                        break;
                }

                position += length;
            }
            else if (wire == 0)
            {
                var value = ReadVarint(buffer, ref position);
                switch (field)
                {
                    case 4:
                        algorithm = value;
                        break;
                    case 5:
                        digits = value;
                        break;
                    case 6:
                        type = value;
                        break;
                    case 7:
                        counter = value;
                        break;
                }
            }
            else
            {
                Skip(buffer, ref position, wire);
            }
        }

        // The name is often "Issuer:account".
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0)
        {
            issuer = issuer.Length > 0 ? issuer : name[..colon];
            name = name[(colon + 1)..];
        }

        if (secret.Length == 0 || algorithm == 4)
        {
            return null;
        }

        var account = new OtpAccount
        {
            Issuer = issuer,
            Label = name.Trim(),
            Secret = Base32.Encode(secret).TrimEnd('='),
            Type = type == 1 ? OtpType.Hotp : OtpType.Totp,
            Algorithm = algorithm switch
            {
                2 => OtpAlgorithm.Sha256,
                3 => OtpAlgorithm.Sha512,
                _ => OtpAlgorithm.Sha1,
            },
            Digits = digits == 2 ? 8 : 6,
            Counter = counter,
        };

        return account.IsValid(out _) ? account : null;
    }

    private static (long Field, int Wire) ReadTag(byte[] data, ref int position)
    {
        var tag = ReadVarint(data, ref position);
        return (tag >> 3, (int)(tag & 7));
    }

    private static int ReadLength(byte[] data, ref int position)
    {
        var length = ReadVarint(data, ref position);
        if (length < 0 || length > data.Length - position)
        {
            throw new FormatException("The length is outside the data.");
        }

        return (int)length;
    }

    private static long ReadVarint(byte[] data, ref int position)
    {
        long value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = data[position++];
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new FormatException("The number is too long.");
    }

    private static void Skip(byte[] data, ref int position, int wire)
    {
        switch (wire)
        {
            case 0:
                ReadVarint(data, ref position);
                break;
            case 1:
                position += 8;
                break;
            case 2:
                position += ReadLength(data, ref position);
                break;
            case 5:
                position += 4;
                break;
            default:
                throw new FormatException("Unknown field type.");
        }

        if (position > data.Length)
        {
            throw new FormatException("The data ends too early.");
        }
    }
}
