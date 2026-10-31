using FluentAssertions;
using SafeAuthenticator.Backup;
using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;
using SafeAuthenticator.Platform;
using Xunit;

namespace SafeAuthenticator.Tests;

public class BackupTests
{
    private static readonly Argon2Parameters FastKdf = new()
    {
        MemorySizeKib = 8192,
        Iterations = 1,
        DegreeOfParallelism = 1,
        KeyLength = 32,
    };

    private static BackupService CreateService()
        => new(new AesGcmEncryptionService(), new MasterPasswordService(), FastKdf);

    [Fact]
    public async Task ExportThenImport_RoundTrips()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"sa-backup-{Guid.NewGuid():N}");
        try
        {
            var provider = new LocalFolderBackupProvider(folder);
            var service = CreateService();
            var accounts = new[]
            {
                new OtpAccount { Issuer = "GitHub", Label = "alice", Secret = "JBSWY3DPEHPK3PXP" },
            };

            var path = await service.ExportAsync(accounts, "pw", provider);
            path.Should().EndWith(".sauth");

            var list = await provider.ListAsync();
            list.Should().ContainSingle();

            var restored = await service.ImportAsync(list[0], "pw", provider);
            restored.Should().ContainSingle();
            restored[0].Secret.Should().Be("JBSWY3DPEHPK3PXP");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Import_WrongPassword_Throws()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"sa-backup-{Guid.NewGuid():N}");
        try
        {
            var provider = new LocalFolderBackupProvider(folder);
            var service = CreateService();
            await service.ExportAsync([new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" }], "right", provider);
            var file = (await provider.ListAsync())[0];

            var act = async () => await service.ImportAsync(file, "wrong", provider);
            await act.Should().ThrowAsync<InvalidPasswordException>();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Export_WritesNoPlaintextSecret()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"sa-backup-{Guid.NewGuid():N}");
        try
        {
            var provider = new LocalFolderBackupProvider(folder);
            await CreateService().ExportAsync([new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" }], "pw", provider);
            var file = Path.Combine(folder, (await provider.ListAsync())[0]);
            var text = await File.ReadAllTextAsync(file);
            text.Should().NotContain("JBSWY3DPEHPK3PXP");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void HeadlessTray_RaisesCommands()
    {
        using var tray = new HeadlessSystemTray();
        tray.IsSupported.Should().BeFalse();
        string? clicked = null;
        tray.MenuItemClicked += (_, id) => clicked = id;
        tray.RequestShowWindow();
        clicked.Should().Be(TrayCommands.ShowWindow);
        TrayCommands.DefaultMenu.Should().HaveCount(5);
    }
}
