// File:    WebDavBackupProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace MasterSafe.Backup;

/// <summary>
/// Stores encrypted <c>.sauth</c> backups on any WebDAV-compatible endpoint (e.g. Nextcloud,
/// ownCloud, or a generic WebDAV server) using <see cref="HttpClient"/>. No vendor SDK is used,
/// so no third-party cloud dependency is introduced. Only encrypted bytes ever leave the device.
/// </summary>
public sealed class WebDavBackupProvider : IBackupProvider
{
    /// <summary>Largest backup that will be downloaded (100 MiB).</summary>
    public const long MaxDownloadBytes = 100L * 1024 * 1024;

    /// <summary>Largest folder listing accepted from a server.</summary>
    public const int MaxListingChars = 4 * 1024 * 1024;

    /// <summary>
    /// Creates a client for a WebDAV server with this login. It follows no redirects, so a server cannot send the login, or
    /// the backups, somewhere else.
    /// </summary>
    public static HttpClient CreateClient(string? user, string? password)
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrEmpty(user))
        {
            var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
        }

        return client;
    }

    /// <summary>Asks the server whether the address and login work. Nothing is written.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default) => await ListAsync(cancellationToken).ConfigureAwait(false);

    private readonly HttpClient _client;
    private readonly Uri _baseUri;

    /// <summary>
    /// Creates a provider for the supplied WebDAV collection URI. The <paramref name="client"/>
    /// should carry any authentication headers (e.g. Basic/Bearer) the endpoint requires.
    /// </summary>
    /// <param name="client">An <see cref="HttpClient"/> preconfigured with credentials.</param>
    /// <param name="collectionUri">The WebDAV collection (folder) URI, ending with a slash.</param>
    public WebDavBackupProvider(HttpClient client, Uri collectionUri)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(collectionUri);

        if (!collectionUri.IsAbsoluteUri
            || (collectionUri.Scheme != Uri.UriSchemeHttps && !(collectionUri.Scheme == Uri.UriSchemeHttp && collectionUri.IsLoopback)))
        {
            throw new ArgumentException(
                "The WebDAV address must start with https:// so the login and backups are protected in transit.",
                nameof(collectionUri));
        }

        if (!string.IsNullOrEmpty(collectionUri.UserInfo))
        {
            throw new ArgumentException("Do not put a user name or password in the address.", nameof(collectionUri));
        }

        _client = client;
        var text = collectionUri.AbsoluteUri;
        _baseUri = text.EndsWith('/') ? collectionUri : new Uri(text + "/");
    }

    /// <inheritdoc />
    public string Name => "WebDAV Cloud";

    /// <inheritdoc />
    public async Task<string> UploadAsync(string fileName, byte[] encryptedContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(encryptedContent);

        var target = new Uri(_baseUri, Uri.EscapeDataString(fileName));
        using var content = new ByteArrayContent(encryptedContent);
        content.Headers.ContentType = new("application/octet-stream");

        using var response = await _client.PutAsync(target, content, cancellationToken).ConfigureAwait(false);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed:
                throw new BackupNameTakenException(fileName);
            case HttpStatusCode.InsufficientStorage:
                throw new BackupProviderException("The server is full. MasterSafe never deletes anything on a server, so remove old files there with the server's own tools.");
        }

        EnsureSuccess(response, $"Upload of '{fileName}' failed");
        return target.AbsoluteUri;
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var target = new Uri(_baseUri, Uri.EscapeDataString(fileName));
        // Read as it arrives, not all at once: a server must not be able to fill the memory before the limit below is checked.
        using var response = await _client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Backup not found.", fileName);
        }

        EnsureSuccess(response, $"Download of '{fileName}' failed");

        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            throw new InvalidDataException("The backup is larger than the allowed limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxDownloadBytes)
            {
                throw new InvalidDataException("The backup is larger than the allowed limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), _baseUri);
        request.Headers.Add("Depth", "1");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "Listing backups failed");

        // The server is outside this computer, so what it sends is limited like any other file from outside.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var body = new char[MaxListingChars + 1];
        var length = 0;
        int read;
        while (length < body.Length && (read = await reader.ReadAsync(body.AsMemory(length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            length += read;
        }

        if (length > MaxListingChars)
        {
            throw new InvalidDataException("The server sent a folder listing that is too large.");
        }

        return ParseFileNames(new string(body, 0, length));
    }

    private static IReadOnlyList<string> ParseFileNames(string propfindXml)
    {
        if (string.IsNullOrWhiteSpace(propfindXml))
        {
            return [];
        }

        XDocument document;
        try
        {
            // No DTDs: a server must not be able to define entities (entity expansion).
            using var text = new StringReader(propfindXml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return [];
        }

        XNamespace dav = "DAV:";
        var names = new List<string>();
        foreach (var href in document.Descendants(dav + "href"))
        {
            var value = href.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.TrimEnd('/');
            var lastSegment = trimmed[(trimmed.LastIndexOf('/') + 1)..];
            var decoded = Uri.UnescapeDataString(lastSegment);
            if (decoded.EndsWith(".sauth", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(decoded);
            }
        }

        return names;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string message)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new BackupProviderException($"{message}: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }
}

/// <summary>The name is already in use. Files are never overwritten, so another name must be chosen.</summary>
public sealed class BackupNameTakenException(string fileName) : Exception($"The name {fileName} is already in use.");

/// <summary>Raised when a backup provider operation fails.</summary>
public sealed class BackupProviderException : Exception
{
    public BackupProviderException(string message) : base(message)
    {
    }

    public BackupProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
