// File:    WebDavBackupProviderTests.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Net;
using System.Text;
using FluentAssertions;
using SafeAuthenticator.Backup;
using Xunit;

namespace SafeAuthenticator.Tests;

public class WebDavBackupProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Buffer content so the request can be inspected after the call.
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync();
            }

            Requests.Add(request);
            return _responder(request);
        }
    }

    private static readonly Uri Collection = new("https://cloud.example.com/remote.php/dav/backups/");

    [Fact]
    public async Task Upload_PutsEncryptedBytesToTarget()
    {
        byte[]? uploaded = null;
        var handler = new StubHandler(req =>
        {
            uploaded = req.Content!.ReadAsByteArrayAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var client = new HttpClient(handler);
        var provider = new WebDavBackupProvider(client, Collection);
        var payload = new byte[] { 1, 2, 3, 4 };

        var location = await provider.UploadAsync("vault-20260101-000000.sauth", payload);

        uploaded.Should().Equal(payload);
        location.Should().Contain("vault-20260101-000000.sauth");
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public async Task Download_ReturnsBytes()
    {
        var expected = new byte[] { 9, 8, 7 };
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        });
        using var client = new HttpClient(handler);
        var provider = new WebDavBackupProvider(client, Collection);

        var bytes = await provider.DownloadAsync("vault.sauth");

        bytes.Should().Equal(expected);
    }

    [Fact]
    public async Task Download_Missing_ThrowsFileNotFound()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var provider = new WebDavBackupProvider(client, Collection);

        var act = () => provider.DownloadAsync("missing.sauth");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task List_ParsesSauthFilesFromPropfind()
    {
        const string xml = """
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response><d:href>/remote.php/dav/backups/</d:href></d:response>
              <d:response><d:href>/remote.php/dav/backups/vault-1.sauth</d:href></d:response>
              <d:response><d:href>/remote.php/dav/backups/notes.txt</d:href></d:response>
              <d:response><d:href>/remote.php/dav/backups/vault-2.sauth</d:href></d:response>
            </d:multistatus>
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.MultiStatus)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        });
        using var client = new HttpClient(handler);
        var provider = new WebDavBackupProvider(client, Collection);

        var names = await provider.ListAsync();

        names.Should().BeEquivalentTo("vault-1.sauth", "vault-2.sauth");
    }

    [Fact]
    public async Task Upload_Failure_ThrowsBackupProviderException()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new HttpClient(handler);
        var provider = new WebDavBackupProvider(client, Collection);

        var act = () => provider.UploadAsync("vault.sauth", [1]);

        await act.Should().ThrowAsync<BackupProviderException>();
    }
}
