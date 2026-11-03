// File:    WebsiteIconFetcher.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using SkiaSharp;

namespace MasterSafe.App.Services;

/// <summary>What became of a request for a site's icon.</summary>
/// <param name="PngBase64">The icon as a 32 by 32 PNG, or null when there is none.</param>
/// <param name="Problem">Why there is none, in plain words.</param>
public sealed record IconResult(string? PngBase64, string? Problem);

/// <summary>Decides what an icon file may be, before any image decoder sees it.</summary>
public static class IconImageCheck
{
    /// <summary>Largest icon file read (256 KiB).</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>The widest or tallest picture accepted.</summary>
    public const int MaxDimension = 512;

    /// <summary>Size of the stored icon.</summary>
    public const int StoredSize = 32;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> IcoSignature => [0, 0, 1, 0];

    /// <summary>True when the file starts as a PNG or ICO. Everything else, SVG above all, is refused.</summary>
    public static bool HasAllowedSignature(ReadOnlySpan<byte> data)
        => data.Length >= 8 && (data.StartsWith(PngSignature) || data.StartsWith(IcoSignature));

    /// <summary>
    /// Turns a file into a 32 by 32 PNG. The downloaded bytes are never kept: only the pixels are, written out again by this
    /// program. Null when the file is not acceptable. Any file can be fed to this, and it must only ever say no.
    /// </summary>
    public static byte[]? Normalize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaxBytes || !HasAllowedSignature(data))
        {
            return null;
        }

        try
        {
            using var stream = new SKMemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width is < 1 or > MaxDimension || codec.Info.Height is < 1 or > MaxDimension
                || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Ico))
            {
                return null;
            }

            using var full = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (full is null)
            {
                return null;
            }

            using var small = full.Resize(new SKImageInfo(StoredSize, StoredSize, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var image = small is null ? null : SKImage.FromBitmap(small);
            using var png = image?.Encode(SKEncodedImageFormat.Png, 100);
            return png?.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NullReferenceException or OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>
/// Fetches the icon a site publishes, for one entry, only when the user asks. These rules are fixed; see the Icons plan in
/// docs/Roadmap.md. It goes straight to the site with no icon service in between. Only <c>https://host/favicon.ico</c> is asked for,
/// without the saved path, query or user name, and without cookies. Only public names are used, and the check runs again on
/// the address the connection really goes to, so a site cannot aim this at the computer or the local network.
/// </summary>
public sealed class WebsiteIconFetcher
{
    /// <summary>The longest a request may take.</summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(5);

    private const string SecurityProblem = "This address cannot be used for an icon. Only public https sites can.";

    private static readonly string[] LocalSuffixes = [".local", ".localhost", ".internal", ".intranet", ".lan", ".home", ".corp", ".home.arpa", ".onion"];

    private readonly HttpMessageHandler _handler;

    /// <summary>Creates a fetcher. Tests pass their own handler; otherwise the strict one is used.</summary>
    public WebsiteIconFetcher(HttpMessageHandler? handler = null) => _handler = handler ?? CreateHandler();

    /// <summary>The name to ask, from an entry's address. Null when it is not a public name.</summary>
    public static string? PublicHostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var text = url.Trim();
        if (!Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.HostNameType != UriHostNameType.Dns)
        {
            return null;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!host.Contains('.', StringComparison.Ordinal) || LocalSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal)))
        {
            return null;
        }

        // A name made of digits and dots is an address written another way: 2130706433, or 0x7f.1.
        var last = host[(host.LastIndexOf('.') + 1)..];
        return last.All(char.IsAsciiDigit) || last.StartsWith("0x", StringComparison.Ordinal) ? null : host;
    }

    /// <summary>True for an address on the public internet. Everything private, local, reserved or special is refused.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && b[1] is 18 or 19)
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        // Only global unicast, 2000::/3, without the ranges that carry another address inside them.
        var v6 = address.GetAddressBytes();
        return (v6[0] & 0xE0) == 0x20
            && !(v6[0] == 0x20 && v6[1] == 0x02)
            && !(v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x00 && v6[3] == 0x00)
            && !(v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0D && v6[3] == 0xB8);
    }

    /// <summary>The one redirect allowed: the same name with or without www, over https, to the same file.</summary>
    public static bool IsAllowedRedirect(string host, Uri? target)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (target is null || !target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps || !target.IsDefaultPort || !string.IsNullOrEmpty(target.UserInfo))
        {
            return false;
        }

        static string Bare(string name) => name.StartsWith("www.", StringComparison.Ordinal) ? name[4..] : name;
        return Bare(target.IdnHost.ToLowerInvariant()) == Bare(host) && target.AbsolutePath == "/favicon.ico" && target.Query.Length == 0;
    }

    /// <summary>Asks the site for its icon. Nothing is stored here: the caller shows a preview and the user decides.</summary>
    public async Task<IconResult> FetchAsync(string? url, CancellationToken cancellationToken = default)
    {
        var host = PublicHostOf(url);
        if (host is null)
        {
            return new IconResult(null, SecurityProblem);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeLimit);
        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

        try
        {
            var target = new Uri($"https://{host}/favicon.ico");
            using var first = await GetAsync(client, target, limit.Token).ConfigureAwait(false);
            if ((int)first.StatusCode is >= 300 and < 400)
            {
                if (!IsAllowedRedirect(host, first.Headers.Location))
                {
                    return new IconResult(null, "The site sent the request somewhere else, so it was stopped.");
                }

                using var second = await GetAsync(client, first.Headers.Location!, limit.Token).ConfigureAwait(false);
                return await ReadAsync(second, limit.Token).ConfigureAwait(false);
            }

            return await ReadAsync(first, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new IconResult(null, "The site took too long to answer.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is AddressRefusedException)
        {
            return new IconResult(null, SecurityProblem);
        }
        catch (HttpRequestException)
        {
            return new IconResult(null, "The site could not be reached with a valid https connection.");
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri target, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IconResult> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new IconResult(null, "The site has no icon at the usual place.");
        }

        if (response.Content.Headers.ContentLength > IconImageCheck.MaxBytes)
        {
            return new IconResult(null, "The icon file is too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > IconImageCheck.MaxBytes)
            {
                return new IconResult(null, "The icon file is too large.");
            }

            memory.Write(buffer, 0, read);
        }

        var png = IconImageCheck.Normalize(memory.ToArray());
        return png is null
            ? new IconResult(null, "The site's icon is not a PNG or ICO picture MasterSafe accepts.")
            : new IconResult(Convert.ToBase64String(png), null);
    }

    private sealed class AddressRefusedException() : IOException("The name leads to an address that is not public.");

    private static SocketsHttpHandler CreateHandler() => new()
    {
        // Each of these is a rule: redirects are checked by hand, no cookies, no proxy that would hide where the request goes,
        // and no stored logins. Certificates are checked by the default rules, with no exceptions.
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PooledConnectionLifetime = TimeSpan.Zero,
        ConnectTimeout = TimeLimit,
        MaxResponseHeadersLength = 32,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = ConnectToPublicAddressAsync,
    };

    // The name is looked up here, and the connection goes to exactly the address that was checked. Checking first and letting
    // the system look the name up again would let a site answer differently the second time.
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || !addresses.All(IsPublicAddress))
        {
            throw new AddressRefusedException();
        }

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, 443), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }

        throw new IOException(string.Create(CultureInfo.InvariantCulture, $"Could not connect to {context.DnsEndPoint.Host}."));
    }
}
