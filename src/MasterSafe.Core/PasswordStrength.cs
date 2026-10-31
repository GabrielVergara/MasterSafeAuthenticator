// File:    PasswordStrength.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

namespace MasterSafe.Core;

/// <summary>How strong a password looks.</summary>
/// <param name="Score">0 (very weak) to 4 (strong).</param>
/// <param name="Label">A word for the score, for example "Good".</param>
/// <param name="Bits">A rough estimate of the guessing effort in bits.</param>
/// <param name="Hint">What would make it better, or null when it is strong.</param>
public sealed record PasswordStrengthResult(int Score, string Label, double Bits, string? Hint);

/// <summary>
/// A small offline strength estimator. It looks at length, the kinds of characters, repeats and simple sequences,
/// very common passwords, and passwords made of several words. It is an estimate to guide people, not a guarantee:
/// a password that is long and random is strong, and one that appears in a leak is weak however it looks.
/// </summary>
public static class PasswordStrength
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "p@ssw0rd", "p@ssword", "pa$$word", "123456", "1234567",
        "12345678", "123456789", "1234567890", "0123456789", "111111", "000000", "123123", "654321", "qwerty", "qwerty123", "qwertyuiop",
        "azerty", "asdfgh", "asdfghjkl", "zxcvbn", "zxcvbnm", "1q2w3e", "1q2w3e4r", "1qaz2wsx", "qazwsx", "zaq12wsx", "abc123", "abcd1234",
        "letmein", "welcome", "welcome1", "admin", "admin123", "administrator", "root", "toor", "login", "master", "hello", "hello123",
        "iloveyou", "monkey", "dragon", "football", "baseball", "soccer", "hockey", "sunshine", "princess", "shadow", "superman",
        "batman", "trustno1", "freedom", "whatever", "starwars", "pokemon", "michael", "jennifer", "jordan", "harley", "ranger",
        "secret", "changeme", "default", "guest", "test", "test123", "testing", "temp", "temp123", "qwe123", "pass", "pass1234",
        "salasana", "salasana1", "salasana123", "contrasena", "contraseña", "motdepasse", "passwort", "mypassword", "letmein1",
        "internet", "computer", "cheese", "summer", "winter", "spring", "autumn", "charlie", "donald", "mustang", "access",
    };

    /// <summary>Estimates the strength of <paramref name="password"/>. <paramref name="related"/> are words such as the title and user name.</summary>
    public static PasswordStrengthResult Evaluate(string? password, params string?[] related)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new PasswordStrengthResult(0, "Empty", 0, "Type or generate a password.");
        }

        if (Common.Contains(password) || Common.Contains(password.TrimEnd('!', '1', '2', '3')))
        {
            return new PasswordStrengthResult(0, "Very weak", 3, "This is one of the most common passwords. Use something long and unique.");
        }

        // Something short repeated ("passwordpassword", "abc123abc123") is only as strong as the part that repeats.
        var block = RepeatedBlock(password);
        if (block is not null)
        {
            var inner = Evaluate(block, related);
            var bitsRepeated = Math.Min(inner.Bits + 1, 40);
            var cap = bitsRepeated < 28 ? 0 : bitsRepeated < 36 ? 1 : 2;
            return new PasswordStrengthResult(
                Math.Min(inner.Score, cap),
                Math.Min(inner.Score, cap) == 0 ? "Very weak" : "Weak",
                Math.Round(bitsRepeated, 1),
                "Repeating something short does not make a password stronger. Use different words or characters.");
        }

        var length = password.Length;
        var pool = 0;
        if (password.Any(char.IsLower))
        {
            pool += 26;
        }

        if (password.Any(char.IsUpper))
        {
            pool += 26;
        }

        if (password.Any(char.IsDigit))
        {
            pool += 10;
        }

        if (password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c < 128))
        {
            pool += 33;
        }

        if (password.Any(c => c >= 128 || char.IsWhiteSpace(c)))
        {
            pool += 60;
        }

        var perChar = Math.Log2(Math.Max(pool, 2));

        // Repeated characters add little: only distinct ones count in full.
        var distinct = password.Distinct().Count();
        var bits = (distinct * perChar) + ((length - distinct) * 1.0);

        // Runs such as "abcd", "4321" or "aaaa" are easy to guess.
        bits -= SequentialCharacters(password) * (perChar - 1);
        bits = Math.Max(bits, 0);

        // Several unrelated words (a passphrase) are strong even though they are only letters.
        var words = password.Split([' ', '-', '_', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && w.All(char.IsLetter))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Count();
        if (words >= 4)
        {
            bits = Math.Max(bits, Math.Min(words * 10.5, length * 4.0));
        }

        string? hint = null;
        foreach (var item in related)
        {
            if (!string.IsNullOrWhiteSpace(item) && item.Trim().Length >= 3
                && password.Contains(item.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                bits = Math.Max(0, bits - 20);
                hint = "It contains the title or user name. Avoid using them in the password.";
                break;
            }
        }

        var score = bits switch
        {
            < 28 => 0,
            < 36 => 1,
            < 60 => 2,
            < 80 => 3,
            _ => 4,
        };

        hint ??= score switch
        {
            <= 1 => "Too short or too simple. Use a longer password, or generate one.",
            2 => "Longer is better. A phrase of four or more random words is strong and easy to remember.",
            3 => "Good. A few more characters would make it strong.",
            _ => null,
        };

        var label = score switch
        {
            0 => "Very weak",
            1 => "Weak",
            2 => "Fair",
            3 => "Good",
            _ => "Strong",
        };

        return new PasswordStrengthResult(score, label, Math.Round(bits, 1), hint);
    }

    /// <summary>The shortest block that, repeated two or more times, makes up the whole password; null when there is none.</summary>
    private static string? RepeatedBlock(string password)
    {
        for (var size = 1; size <= password.Length / 2; size++)
        {
            if (password.Length % size != 0)
            {
                continue;
            }

            var block = password[..size];
            if (password.Length / size >= 2 && string.Concat(Enumerable.Repeat(block, password.Length / size)) == password)
            {
                return block;
            }
        }

        return null;
    }

    /// <summary>Counts characters that continue an ascending, descending or repeated run (three or more in a row).</summary>
    private static int SequentialCharacters(string password)
    {
        var count = 0;
        var run = 1;
        var step = 0;
        for (var i = 1; i < password.Length; i++)
        {
            var diff = password[i] - password[i - 1];
            if (diff is -1 or 0 or 1 && (run == 1 || diff == step))
            {
                step = diff;
                run++;
                if (run >= 3)
                {
                    count++;
                }
            }
            else
            {
                run = 1;
                step = 0;
                if (diff is -1 or 0 or 1)
                {
                    step = diff;
                    run = 2;
                }
            }
        }

        return count;
    }
}
