// File:    Release17Tests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>Fixes found while testing 1.7.</summary>
public sealed class Release17Tests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms17-" + Guid.NewGuid().ToString("N"));

    public Release17Tests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task TheKeePassExport_AlwaysSaysWhatHappened()
    {
        var dialogs = new Dialogs();
        var backup = new BackupViewModel { Dialogs = dialogs, ContentsProvider = () => new VaultContents(), Folder = _folder };

        await backup.ExportKdbxCommand.ExecuteAsync(null);
        backup.Status.Should().Contain("at least 12 characters", "a short or empty password must not look like a dead button");

        backup.MasterPassword = "a-long-enough-password";
        await backup.ExportKdbxCommand.ExecuteAsync(null);
        backup.Status.Should().Contain("cancelled");
        dialogs.StartFolder.Should().Be(_folder, "the dialog opens in the backup folder");

        dialogs.SavePath = Path.Combine(_folder, "missing-folder", "x.kdbx");
        await backup.ExportKdbxCommand.ExecuteAsync(null);
        backup.Status.Should().StartWith("Export failed:");

        backup.ContentsProvider = () => throw new InvalidOperationException("boom");
        dialogs.SavePath = Path.Combine(_folder, "x.kdbx");
        await backup.ExportKdbxCommand.ExecuteAsync(null);
        backup.Status.Should().Be("Export failed: boom", "an unexpected error is shown, not swallowed");
        backup.IsWorking.Should().BeFalse();

        backup.ContentsProvider = () => new VaultContents { Passwords = [new PasswordEntry { Title = "T", Password = "p" }] };
        await backup.ExportKdbxCommand.ExecuteAsync(null);
        File.Exists(dialogs.SavePath).Should().BeTrue();
        backup.Status.Should().StartWith("Saved x.kdbx");
    }

    [Fact]
    public async Task TheBackupFolder_CanBeChosenInTheFolderDialog()
    {
        var backup = new BackupViewModel { Dialogs = new Dialogs { PickedFolder = _folder } };

        await backup.ChooseFolderCommand.ExecuteAsync(null);

        backup.Folder.Should().Be(_folder);
    }

    private sealed class Dialogs : IFileDialogs
    {
        public string? SavePath { get; set; }

        public string? PickedFolder { get; set; }

        public string? StartFolder { get; private set; }

        public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string? startFolder = null)
        {
            StartFolder = startFolder;
            return Task.FromResult(SavePath);
        }

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(PickedFolder);

        public void ShowInFolder(string path)
        {
        }
    }
}
