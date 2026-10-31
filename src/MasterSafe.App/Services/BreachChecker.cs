// File:    BreachChecker.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace MasterSafe.App.Services;

/// <summary>
/// Asks the Have I Been Pwned "Pwned Passwords" service whether a password appears in known data breaches.
/// <para>
/// The password never leaves the computer. Its SHA-1 hash is computed here and only the first five characters of that hash
/// are sent; the service answers with every hash that starts the same way, and the comparison is made here. Responses are
/// padded by the service so their size reveals nothing. This is optional and off by default.
/// </para>
/// </summary>
public sealed class BreachChecker(HttpClient http)
{
    /// <summary>The service address. Only https is used.</summary>
    public const string Endpoint = "https://api.pwnedpasswords.com/range/";

    private const int MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>Creates a client with a short timeout and an honest user agent.</summary>
    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MasterSafe-Authenticator", AppInfo.Version));
        return client;
    }

    /// <summary>How many times the password was seen in breaches; 0 when it was not found.</summary>
    /// <exception cref="HttpRequestException">The service could not be reached or answered with an error.</exception>
    public async Task<int> CountAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        // SHA-1 is what the service is built on; it is used here only to look a password up, never to protect anything.
#pragma warning disable CA5350
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = hash[..5];
        var suffix = hash[5..];

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + prefix);
        request.Headers.Add("Add-Padding", "true");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (limited.Length + read > MaxResponseBytes)
            {
                throw new HttpRequestException("The breach service sent more data than expected.");
            }

            limited.Write(buffer, 0, read);
        }

        foreach (var line in Encoding.ASCII.GetString(limited.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon != 35 || !string.Equals(line[..colon], suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line[(colon + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? Math.Max(0, count)
                : 0;
        }

        return 0;
    }
}
