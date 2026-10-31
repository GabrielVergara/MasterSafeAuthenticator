using System.Globalization;

namespace SafeAuthenticator.Core;

/// <summary>
/// Parses and builds <c>otpauth://</c> URIs as defined by the Key Uri Format.
/// </summary>
public static class OtpAuthParser
{
    /// <summary>
    /// Parses an <c>otpauth://totp</c> or <c>otpauth://hotp</c> URI into an <see cref="OtpAccount"/>.
    /// </summary>
    /// <exception cref="FormatException">Thrown when the URI is malformed or unsupported.</exception>
    public static OtpAccount Parse(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.Scheme, "otpauth", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("URI scheme must be 'otpauth'.");
        }

        var type = parsed.Host.ToLowerInvariant() switch
        {
            "totp" => OtpType.Totp,
            "hotp" => OtpType.Hotp,
            _ => throw new FormatException($"Unsupported OTP type '{parsed.Host}'."),
        };

        var label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
        string issuerFromLabel = string.Empty;
        string account = label;
        var colon = label.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            issuerFromLabel = label[..colon].Trim();
            account = label[(colon + 1)..].Trim();
        }

        var query = ParseQuery(parsed.Query);
        var secret = query.GetValueOrDefault("secret");
        if (string.IsNullOrWhiteSpace(secret) || !Base32.IsValid(secret))
        {
            throw new FormatException("Missing or invalid 'secret' parameter.");
        }

        var issuer = query.GetValueOrDefault("issuer");
        if (string.IsNullOrWhiteSpace(issuer))
        {
            issuer = issuerFromLabel;
        }

        var algorithm = ParseAlgorithm(query.GetValueOrDefault("algorithm"));
        var digits = ParseInt(query.GetValueOrDefault("digits"), 6);
        if (digits is < 6 or > 8)
        {
            throw new FormatException("Digits must be between 6 and 8.");
        }

        var period = ParseInt(query.GetValueOrDefault("period"), 30);
        if (period <= 0)
        {
            throw new FormatException("Period must be positive.");
        }

        long counter = 0;
        if (type == OtpType.Hotp)
        {
            var counterStr = query.GetValueOrDefault("counter");
            if (string.IsNullOrWhiteSpace(counterStr) || !long.TryParse(counterStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out counter))
            {
                throw new FormatException("HOTP URIs require a valid 'counter' parameter.");
            }
        }

        return new OtpAccount
        {
            Issuer = issuer ?? string.Empty,
            Label = account,
            Secret = secret.Trim(),
            Type = type,
            Algorithm = algorithm,
            Digits = digits,
            Period = period,
            Counter = counter,
        };
    }

    /// <summary>
    /// Attempts to parse an otpauth URI without throwing.
    /// </summary>
    public static bool TryParse(string uri, out OtpAccount? account)
    {
        try
        {
            account = Parse(uri);
            return true;
        }
        catch (FormatException)
        {
            account = null;
            return false;
        }
        catch (ArgumentNullException)
        {
            account = null;
            return false;
        }
    }

    /// <summary>
    /// Builds an otpauth URI from an account.
    /// </summary>
    public static string Build(OtpAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var typeStr = account.Type == OtpType.Hotp ? "hotp" : "totp";
        var label = string.IsNullOrEmpty(account.Issuer)
            ? account.Label
            : $"{account.Issuer}:{account.Label}";

        var query = new List<string>
        {
            $"secret={Uri.EscapeDataString(account.Secret)}",
        };

        if (!string.IsNullOrEmpty(account.Issuer))
        {
            query.Add($"issuer={Uri.EscapeDataString(account.Issuer)}");
        }

        query.Add($"algorithm={account.Algorithm.ToString().ToUpperInvariant()}");
        query.Add($"digits={account.Digits.ToString(CultureInfo.InvariantCulture)}");

        if (account.Type == OtpType.Totp)
        {
            query.Add($"period={account.Period.ToString(CultureInfo.InvariantCulture)}");
        }
        else
        {
            query.Add($"counter={account.Counter.ToString(CultureInfo.InvariantCulture)}");
        }

        return $"otpauth://{typeStr}/{Uri.EscapeDataString(label)}?{string.Join('&', query)}";
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.StartsWith('?') ? query[1..] : query;
        if (string.IsNullOrEmpty(trimmed))
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            }
            else
            {
                var key = Uri.UnescapeDataString(pair[..eq]);
                var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                result[key] = value;
            }
        }

        return result;
    }

    private static OtpAlgorithm ParseAlgorithm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OtpAlgorithm.Sha1;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "SHA1" => OtpAlgorithm.Sha1,
            "SHA256" => OtpAlgorithm.Sha256,
            "SHA512" => OtpAlgorithm.Sha512,
            _ => throw new FormatException($"Unsupported algorithm '{value}'."),
        };
    }

    private static int ParseInt(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"Invalid integer value '{value}'.");
        }

        return result;
    }
}
