// File:    WebDavBackupProvider.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Net;
using System.Xml.Linq;

namespace SafeAuthenticator.Backup;

/// <summary>
/// Stores encrypted <c>.sauth</c> backups on any WebDAV-compatible endpoint (e.g. Nextcloud,
/// ownCloud, or a generic WebDAV server) using <see cref="HttpClient"/>. No vendor SDK is used,
/// so no third-party cloud dependency is introduced. Only encrypted bytes ever leave the device.
/// </summary>
public sealed class WebDavBackupProvider : IBackupProvider
{
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
        EnsureSuccess(response, $"Upload of '{fileName}' failed");
        return target.AbsoluteUri;
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var target = new Uri(_baseUri, Uri.EscapeDataString(fileName));
        using var response = await _client.GetAsync(target, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Backup not found.", fileName);
        }

        EnsureSuccess(response, $"Download of '{fileName}' failed");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), _baseUri);
        request.Headers.Add("Depth", "1");

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "Listing backups failed");

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseFileNames(body);
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
            document = XDocument.Parse(propfindXml);
        }
        catch (System.Xml.XmlException)
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
