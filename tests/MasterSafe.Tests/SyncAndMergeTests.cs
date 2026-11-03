// File:    SyncAndMergeTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>
/// A server with the rules of the real one at dav.gabriel-vergara.com: only names like vault-YYYYMMDD-HHMMSS.sauth, nothing is
/// ever overwritten (409), DELETE is not allowed (405), and there is a total size limit (507).
/// </summary>
internal sealed class AppendOnlyServer : HttpMessageHandler
{
    private static readonly Regex Name = new(@"\A(vault|recovery)-[0-9]{8}-[0-9]{6}\.sauth\z", RegexOptions.CultureInvariant);

    public Dictionary<string, byte[]> Files { get; } = [];

    public long MaxTotalBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public int Deletes { get; private set; }

    public int Overwrites { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var name = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath.Split('/')[^1]);
        switch (request.Method.Method)
        {
            case "PROPFIND":
                var xml = new StringBuilder("<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/backups/</d:href></d:response>");
                foreach (var file in Files.Keys)
                {
                    xml.Append($"<d:response><d:href>/backups/{file}</d:href></d:response>");
                }

                return new HttpResponseMessage(HttpStatusCode.MultiStatus) { Content = new StringContent(xml.Append("</d:multistatus>").ToString()) };
            case "GET":
                return Files.TryGetValue(name, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            case "PUT":
                if (!Name.IsMatch(name))
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden);
                }

                if (Files.ContainsKey(name))
                {
                    Overwrites++;
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                }

                var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                if (Files.Values.Sum(f => (long)f.Length) + body.Length > MaxTotalBytes)
                {
                    return new HttpResponseMessage(HttpStatusCode.InsufficientStorage);
                }

                Files[name] = body;
                return new HttpResponseMessage(HttpStatusCode.Created);
            default:
                Deletes += request.Method == HttpMethod.Delete ? 1 : 0;
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }
}

public sealed class SyncAndMergeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-sync-" + Guid.NewGuid().ToString("N"));
    private readonly AppendOnlyServer _server = new();
    private readonly byte[] _key = SyncCode.NewKey();
    private DateTime _time = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private WebDavBackupProvider Provider() => new(new HttpClient(_server), new Uri("https://dav.example.org/backups/"));

    private SyncService Service(int step = 1) => new(new AesGcmEncryptionService(), () => _time = _time.AddMinutes(step));

    private SyncSettings Settings() => new() { Key = Convert.ToBase64String(_key), UseWebDav = true, WebDavUrl = "https://dav.example.org/backups/" };

    private static PasswordEntry Entry(string title, string password = "pw-one-long-enough", DateTimeOffset? modified = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = title,
        Password = password,
        ModifiedUtc = modified ?? DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
    };

    private static OtpAccount Account(string issuer) => new() { Issuer = issuer, Secret = "JBSWY3DPEHPK3PXP" };

    // ---------------------------------------------------------------- merging

    [Fact]
    public void ItemsOnOneSide_AreKept()
    {
        var (merged, summary) = VaultMerger.Merge(
            new VaultContents { Passwords = [Entry("Mine")], Accounts = [Account("A")] },
            new VaultContents { Passwords = [Entry("Theirs")], Accounts = [Account("B")] });

        merged.Passwords.Select(p => p.Title).Should().BeEquivalentTo("Mine", "Theirs");
        merged.Accounts.Should().HaveCount(2);
        summary.Added.Should().Be(2);
    }

    [Fact]
    public void WhenBothSidesChangedAnItem_TheNewerWins_AndTheOlderPasswordIsKeptInHistory()
    {
        var id = Guid.NewGuid();
        var older = Entry("Site", "older-password", DateTimeOffset.Parse("2026-10-01T00:00:00Z"), id);
        var newer = Entry("Site", "newer-password", DateTimeOffset.Parse("2026-10-02T00:00:00Z"), id);

        var (merged, summary) = VaultMerger.Merge(new VaultContents { Passwords = [older] }, new VaultContents { Passwords = [newer] });

        var entry = merged.Passwords.Single();
        entry.Password.Should().Be("newer-password");
        entry.History.Should().ContainSingle(v => v.Password == "older-password");
        summary.Conflicts.Should().Be(1);
    }

    [Fact]
    public void Merging_GivesTheSameResultInEitherOrder_AndTwiceChangesNothing()
    {
        var id = Guid.NewGuid();
        var a = new VaultContents { Passwords = [Entry("Site", "first", DateTimeOffset.Parse("2026-10-01T00:00:00Z"), id), Entry("Only A")] };
        var b = new VaultContents { Passwords = [Entry("Site", "second", DateTimeOffset.Parse("2026-10-01T00:00:00Z"), id), Entry("Only B")], Folders = ["Work"] };

        var (ab, _) = VaultMerger.Merge(a, b);
        var (ba, _) = VaultMerger.Merge(b, a);
        var (again, summary) = VaultMerger.Merge(ab, ba);

        VaultMerger.StateHash(ab).Should().Be(VaultMerger.StateHash(ba), "two computers must end up equal");
        VaultMerger.StateHash(again).Should().Be(VaultMerger.StateHash(ab));
        summary.NothingChanged.Should().BeTrue();
    }

    [Fact]
    public void AnItemDeletedForGood_StaysDeleted_ButAnEditAfterTheDeletionKeepsIt()
    {
        var id = Guid.NewGuid();
        var deleted = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
        var mine = new VaultContents { Tombstones = [new Tombstone { Id = id, DeletedUtc = deleted }] };

        VaultMerger.Merge(mine, new VaultContents { Passwords = [Entry("Gone", modified: deleted.AddDays(-1), id: id)] }).Merged.Passwords.Should().BeEmpty();
        VaultMerger.Merge(mine, new VaultContents { Passwords = [Entry("Edited later", modified: deleted.AddDays(1), id: id)] }).Merged.Passwords.Should().ContainSingle();
    }

    [Fact]
    public void ARestoredEntry_BeatsTheCopyThatIsStillInTheBin()
    {
        var entry = Entry("Back");
        var binned = entry.WithDeleted(DateTimeOffset.Parse("2026-10-05T00:00:00Z"));
        var restored = binned.WithDeleted(null);

        var (merged, _) = VaultMerger.Merge(new VaultContents { Passwords = [binned] }, new VaultContents { Passwords = [restored] });

        merged.Passwords.Single().IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void AnHotpCounter_NeverGoesBack()
    {
        var id = Guid.NewGuid();
        var low = new OtpAccount { Id = id, Issuer = "H", Secret = "JBSWY3DPEHPK3PXP", Type = OtpType.Hotp, Counter = 3 };
        var high = new OtpAccount { Id = id, Issuer = "H", Secret = "JBSWY3DPEHPK3PXP", Type = OtpType.Hotp, Counter = 9, ModifiedUtc = DateTimeOffset.MinValue };

        VaultMerger.Merge(new VaultContents { Accounts = [low] }, new VaultContents { Accounts = [high] }).Merged.Accounts.Single().Counter.Should().Be(9);
        VaultMerger.Merge(new VaultContents { Accounts = [high] }, new VaultContents { Accounts = [low] }).Merged.Accounts.Single().Counter.Should().Be(9);
    }

    [Fact]
    public void AttachedFiles_ComeAlong_AndTheVaultLimitHolds()
    {
        var attachment = new PasswordAttachment { Name = "a.txt", Size = 3 };
        var entry = Entry("With file").WithAttachments([attachment]);
        var remote = new VaultContents { Passwords = [entry], AttachmentData = new Dictionary<Guid, byte[]> { [attachment.Id] = [1, 2, 3] } };

        var (merged, _) = VaultMerger.Merge(new VaultContents(), remote);

        merged.AttachmentData[attachment.Id].Should().Equal(1, 2, 3);

        var big = Enumerable.Range(0, 12).Select(_ => new PasswordAttachment { Name = "b", Size = 5 * 1024 * 1024 }).ToList();
        var huge = new VaultContents
        {
            Passwords = big.Select(a => Entry("x").WithAttachments([a])).ToList(),
            AttachmentData = big.ToDictionary(a => a.Id, _ => new byte[5 * 1024 * 1024]),
        };
        var (limited, _) = VaultMerger.Merge(new VaultContents(), huge);
        limited.AttachmentData.Values.Sum(b => (long)b.Length).Should().BeLessThanOrEqualTo(AttachmentLimits.MaxTotalBytes);
        VaultContentsCodec.Encode(limited).Should().NotBeEmpty("the result must still be a vault that can be saved");
    }

    // ---------------------------------------------------------------- sync files

    [Fact]
    public void TheSyncCode_SurvivesTypingAndCatchesMistakes()
    {
        var code = SyncCode.Format(_key);

        SyncCode.TryParse(code).Should().Equal(_key);
        SyncCode.TryParse(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)).Should().Equal(_key);
        SyncCode.TryParse(code[..^2] + (code[^1] == 'A' ? "BB" : "AA")).Should().BeNull("a mistyped code must be noticed");
        SyncCode.TryParse("hello").Should().BeNull();
        SyncCode.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void EveryChangeToTheLastLetter_IsNoticed()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        for (var round = 0; round < 200; round++)
        {
            var code = SyncCode.Format(SyncCode.NewKey());
            foreach (var letter in alphabet.Where(l => l != code[^1]))
            {
                SyncCode.TryParse(code[..^1] + letter).Should().BeNull();
            }
        }
    }

    [Fact]
    public void ASyncFile_HoldsNoSettingsNoKeyAndNoPlainText()
    {
        var encryption = new AesGcmEncryptionService();
        var contents = new VaultContents
        {
            Passwords = [Entry("Visible title", "very-secret-password")],
            Sync = Settings() with { WebDavPassword = "server-password" },
            Security = new VaultSecurity(),
            RecoveryKeyFingerprint = "abc",
        };

        var bytes = SyncPackage.Seal(contents, _key, "vault-20261102-100000.sauth", encryption);
        var opened = SyncPackage.Open(bytes, _key, "vault-20261102-100000.sauth", encryption);

        Encoding.UTF8.GetString(bytes).Should().NotContain("Visible title").And.NotContain("very-secret-password");
        opened.Passwords.Single().Password.Should().Be("very-secret-password");
        opened.Sync.Should().BeNull();
        opened.Security.Should().BeNull();
        opened.RecoveryKeyFingerprint.Should().BeNull();
    }

    [Fact]
    public void ASyncFileRenamedOrChangedByTheServer_IsRefused()
    {
        var encryption = new AesGcmEncryptionService();
        var bytes = SyncPackage.Seal(new VaultContents { Passwords = [Entry("A")] }, _key, "vault-20261102-100000.sauth", encryption);

        var renamed = () => SyncPackage.Open(bytes, _key, "vault-20261102-100001.sauth", encryption);
        renamed.Should().Throw<InvalidPasswordException>();

        var changed = (byte[])bytes.Clone();
        changed[^1] ^= 1;
        var tampered = () => SyncPackage.Open(changed, _key, "vault-20261102-100000.sauth", encryption);
        tampered.Should().Throw<InvalidPasswordException>();

        var wrongKey = () => SyncPackage.Open(bytes, SyncCode.NewKey(), "vault-20261102-100000.sauth", encryption);
        wrongKey.Should().Throw<InvalidPasswordException>();
    }

    // ---------------------------------------------------------------- whole syncs against the real server's rules

    [Fact]
    public async Task TwoComputers_EndUpTheSame_ThroughAServerThatNeverDeletesOrOverwrites()
    {
        var one = new VaultContents { Passwords = [Entry("From one")], Sync = Settings() };
        var two = new VaultContents { Passwords = [Entry("From two")], Sync = Settings() };

        one = (await Service().RunAsync(one, Provider())).Contents;
        two = (await Service().RunAsync(two, Provider())).Contents;
        one = (await Service().RunAsync(one, Provider())).Contents;

        one.Passwords.Select(p => p.Title).Should().BeEquivalentTo("From one", "From two");
        two.Passwords.Select(p => p.Title).Should().BeEquivalentTo("From one", "From two");
        VaultMerger.StateHash(one).Should().Be(VaultMerger.StateHash(two));
        _server.Deletes.Should().Be(0, "the server refuses deletion, and the app must not even try");
    }

    [Fact]
    public async Task WhenNothingChanged_NoFileIsWritten_SoTwoComputersDoNotAnswerEachOtherForEver()
    {
        var one = new VaultContents { Passwords = [Entry("Same")], Sync = Settings() };
        var two = new VaultContents { Sync = Settings() };

        one = (await Service().RunAsync(one, Provider())).Contents;
        two = (await Service().RunAsync(two, Provider())).Contents;
        var count = _server.Files.Count;

        for (var i = 0; i < 4; i++)
        {
            one = (await Service().RunAsync(one, Provider())).Contents;
            two = (await Service().RunAsync(two, Provider())).Contents;
        }

        _server.Files.Should().HaveCount(count);
    }

    [Fact]
    public async Task ANameThatIsTaken_IsNeverOverwritten_TheNextSecondIsUsed()
    {
        _time = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
        var taken = $"vault-{_time:yyyyMMdd-HHmmss}.sauth";
        _server.Files[taken] = "an ordinary backup"u8.ToArray();

        var service = new SyncService(new AesGcmEncryptionService(), () => new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc));
        var result = await service.RunAsync(new VaultContents { Passwords = [Entry("A")], Sync = Settings() }, Provider());

        result.Uploaded.Should().NotBeNull().And.NotBe(taken);
        _server.Files[taken].Should().Equal("an ordinary backup"u8.ToArray());
    }

    [Fact]
    public async Task OrdinaryBackupsInTheSameFolder_AreIgnored()
    {
        _server.Files["vault-20260101-000000.sauth"] = "{\"Version\":4}"u8.ToArray();

        var result = await Service().RunAsync(new VaultContents { Sync = Settings() }, Provider());

        result.Rejected.Should().Be(0);
        result.Accepted.Should().Be(0);
        result.Contents.Sync!.SeenFiles.Should().Contain("vault-20260101-000000.sauth");
    }

    [Fact]
    public async Task FilesMadeWithAnotherCode_AreNotUsed_AndNothingIsWrittenOverThem()
    {
        var other = new VaultContents { Passwords = [Entry("Someone else")], Sync = Settings() with { Key = Convert.ToBase64String(SyncCode.NewKey()) } };
        await Service().RunAsync(other, Provider());
        var before = _server.Files.Count;

        var result = await Service().RunAsync(new VaultContents { Passwords = [Entry("Mine")], Sync = Settings() }, Provider());

        result.Rejected.Should().Be(1);
        result.Contents.Passwords.Should().ContainSingle().Which.Title.Should().Be("Mine");
        _server.Files.Should().HaveCount(before, "a wrong code must not add files to someone else's sync");
    }

    [Fact]
    public async Task AFullServer_DoesNotLoseWhatWasMerged_AndSaysSo()
    {
        var one = new VaultContents { Passwords = [Entry("From one")], Sync = Settings() };
        await Service().RunAsync(one, Provider());
        _server.MaxTotalBytes = 1;

        var result = await Service().RunAsync(new VaultContents { Passwords = [Entry("From two")], Sync = Settings() }, Provider());

        result.Contents.Passwords.Should().HaveCount(2);
        result.UploadError.Should().Contain("full");
        result.Contents.Sync!.LastStateHash.Should().BeNull("the next sync must try to write again");
    }

    [Fact]
    public async Task AnEmptyVault_WritesNothing()
    {
        var result = await Service().RunAsync(new VaultContents { Sync = Settings() }, Provider());

        result.Uploaded.Should().BeNull();
        _server.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task AFolder_KeepsItsNewestSyncFiles_AndNeverTouchesOtherFiles()
    {
        Directory.CreateDirectory(_folder);
        var backup = Path.Combine(_folder, "vault-20200101-000000.sauth");
        await File.WriteAllTextAsync(backup, "{\"Version\":4}");
        var provider = new LocalFolderBackupProvider(_folder);
        var contents = new VaultContents { Sync = Settings() with { UseWebDav = false, Folder = _folder } };

        for (var i = 0; i < SyncService.KeepInFolder + 5; i++)
        {
            contents = (await Service().RunAsync(contents with { Passwords = [.. contents.Passwords, Entry("Item " + i)] }, provider)).Contents;
        }

        File.Exists(backup).Should().BeTrue("only sync files are removed");
        Directory.GetFiles(_folder, "*.sauth").Should().HaveCount(SyncService.KeepInFolder + 1);
    }

    [Fact]
    public async Task AFolderProvider_NeverOverwrites()
    {
        var provider = new LocalFolderBackupProvider(_folder);
        await provider.UploadAsync("vault-20260101-000000.sauth", [1]);

        var again = () => provider.UploadAsync("vault-20260101-000000.sauth", [2]);

        await again.Should().ThrowAsync<BackupNameTakenException>();
        (await provider.DownloadAsync("vault-20260101-000000.sauth")).Should().Equal(1);
        Directory.GetFiles(_folder).Should().ContainSingle("no temporary file is left behind");
    }

    [Fact]
    public async Task TheWebDavClient_FollowsNoRedirects()
    {
        var target = new TcpRedirectServer();
        using var client = WebDavBackupProvider.CreateClient("user", "secret-password");
        var provider = new WebDavBackupProvider(client, target.Uri);

        var listing = () => provider.ListAsync();

        await listing.Should().ThrowAsync<BackupProviderException>("a redirect is an error here, never followed");
        target.Connections.Should().Be(1);
        target.Dispose();
    }

    /// <summary>Answers every connection with a redirect to itself.</summary>
    private sealed class TcpRedirectServer : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);

        public TcpRedirectServer()
        {
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public int Connections { get; private set; }

        public Uri Uri => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/dav/");

        private async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    Connections++;
                    var stream = client.GetStream();
                    _ = await stream.ReadAtLeastAsync(new byte[4096], 1, throwOnEndOfStream: false);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {Uri}next\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                // The test ended.
            }
        }

        public void Dispose() => _listener.Stop();
    }

    // ---------------------------------------------------------------- full backups

    [Fact]
    public async Task AFullBackup_HoldsEverything_AndAWrongPasswordIsRefused()
    {
        var service = new BackupService(new AesGcmEncryptionService(), new MasterPasswordService(), new Argon2Parameters { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1 });
        var provider = new LocalFolderBackupProvider(_folder);
        var attachment = new PasswordAttachment { Name = "a.txt", Size = 2 };
        var contents = new VaultContents
        {
            Accounts = [Account("GitHub")],
            Passwords = [Entry("Mail").WithAttachments([attachment])],
            AttachmentData = new Dictionary<Guid, byte[]> { [attachment.Id] = [7, 8] },
            Security = new VaultSecurity(),
        };

        var location = await service.ExportContentsAsync(contents, "a-long-backup-password", provider);
        var name = Path.GetFileName(location);
        var read = await service.ImportContentsAsync(name, "a-long-backup-password", provider);

        read.Accounts.Should().ContainSingle();
        read.Passwords.Should().ContainSingle().Which.Title.Should().Be("Mail");
        read.AttachmentData[attachment.Id].Should().Equal(7, 8);
        read.Security.Should().BeNull("settings belong to a vault, not to its data");

        var wrong = () => service.ImportContentsAsync(name, "the-wrong-password", provider);
        await wrong.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task ABackupFromBeforeTheFullFormat_StillOpens()
    {
        var service = new BackupService(new AesGcmEncryptionService(), new MasterPasswordService(), new Argon2Parameters { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1 });
        var provider = new LocalFolderBackupProvider(_folder);

        var location = await service.ExportAsync([Account("Old")], "a-long-backup-password", provider);

        (await service.ImportContentsAsync(Path.GetFileName(location), "a-long-backup-password", provider)).Accounts.Should().ContainSingle();
    }

    [Fact]
    public async Task ABackupAskingForAbsurdKeySettings_IsRefusedBeforeAnyWork()
    {
        var provider = new LocalFolderBackupProvider(_folder);
        await provider.UploadAsync(
            "vault-20260101-000000.sauth",
            Encoding.UTF8.GetBytes("""{"Version":4,"Salt":"AAAAAAAAAAAAAAAAAAAAAA==","KdfParameters":{"MemorySizeKib":2147483647,"Iterations":100000,"DegreeOfParallelism":1,"KeyLength":32},"Nonce":"","Tag":"","Ciphertext":""}"""));
        var service = new BackupService(new AesGcmEncryptionService(), new MasterPasswordService());

        var open = () => service.ImportContentsAsync("vault-20260101-000000.sauth", "x", provider);

        await open.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ASyncFileOnTheBackupList_GivesAClearAnswer()
    {
        var provider = new LocalFolderBackupProvider(_folder);
        await provider.UploadAsync("vault-20260101-000000.sauth", SyncPackage.Seal(new VaultContents(), _key, "x", new AesGcmEncryptionService()));
        var service = new BackupService(new AesGcmEncryptionService(), new MasterPasswordService());

        var open = () => service.ImportContentsAsync("vault-20260101-000000.sauth", "x", provider);

        await open.Should().ThrowAsync<InvalidDataException>().WithMessage("*sync file*");
    }
}
