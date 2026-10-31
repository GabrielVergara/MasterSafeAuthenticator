// File:    PasswordAttachmentAndIconTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using MasterSafe.Core.Import;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

public sealed class PasswordAttachmentAndIconTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-pw-attach-" + Guid.NewGuid().ToString("N"));

    public PasswordAttachmentAndIconTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private string File_(string name, int size, byte fill = 7)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, size).ToArray());
        return path;
    }

    private static PasswordsViewModel Vm()
    {
        var vm = new PasswordsViewModel();
        vm.Load([]);
        return vm;
    }

    private static void SaveEntry(PasswordsViewModel vm, string title)
    {
        vm.Title = title;
        vm.Password = "pw";
        vm.SaveCommand.Execute(null);
    }

    // ------------------------------------------------------------------ attachments

    [Fact]
    public void AttachingAFile_ShowsItInTheForm_AndKeepsItWithTheEntryWhenSaved()
    {
        var vm = Vm();

        vm.AttachFile(File_("doc.txt", 1000)).Should().BeTrue();
        vm.FormAttachments.Should().ContainSingle().Which.Name.Should().Be("doc.txt");
        SaveEntry(vm, "Site");

        var entry = vm.Snapshot().Single();
        entry.Attachments.Should().ContainSingle().Which.Size.Should().Be(1000);
        vm.AttachmentSnapshot()[entry.Attachments[0].Id].Should().HaveCount(1000);
        vm.FormAttachments.Should().BeEmpty("the form is cleared after saving");
        vm.Rows.Single().HasAttachments.Should().BeTrue();
        vm.Rows.Single().AttachmentsText.Should().Be("1 attached file");
    }

    [Fact]
    public void AFileOverFiveMegabytes_IsRefusedWithAClearMessage()
    {
        var vm = Vm();

        vm.AttachFile(File_("big.bin", (int)AttachmentLimits.MaxFileBytes + 1)).Should().BeFalse();

        vm.Status.Should().Contain("big.bin").And.Contain("5 MB");
        vm.FormAttachments.Should().BeEmpty();
        vm.AttachmentBytes.Should().Be(0);
    }

    [Fact]
    public void AFileExactlyAtTheLimit_IsAccepted()
        => Vm().AttachFile(File_("exact.bin", (int)AttachmentLimits.MaxFileBytes)).Should().BeTrue();

    [Fact]
    public void ThePerVaultLimit_StopsFurtherFiles()
    {
        var vm = Vm();
        var path = File_("five.bin", (int)AttachmentLimits.MaxFileBytes);
        for (var i = 0; i < 10; i++)
        {
            vm.AttachFile(path).Should().BeTrue();
        }

        vm.AttachFile(path).Should().BeFalse();

        vm.Status.Should().Contain("limit");
        vm.AttachmentBytes.Should().Be(AttachmentLimits.MaxTotalBytes);
    }

    [Fact]
    public void AMissingFile_IsReported()
    {
        var vm = Vm();

        vm.AttachFile(Path.Combine(_folder, "nope.txt")).Should().BeFalse();

        vm.Status.Should().Contain("does not exist");
    }

    [Fact]
    public void RemovingAFileFromTheForm_AndSaving_DropsItsBytes()
    {
        var vm = Vm();
        vm.AttachFile(File_("a.bin", 100));
        SaveEntry(vm, "Site");
        vm.EditCommand.Execute(vm.Snapshot().Single());
        vm.FormAttachments.Should().ContainSingle();

        vm.RemoveAttachmentCommand.Execute(vm.FormAttachments[0]);
        vm.SaveCommand.Execute(null);

        vm.Snapshot().Single().Attachments.Should().BeEmpty();
        vm.AttachmentBytes.Should().Be(0, "nothing refers to the bytes any more");
    }

    [Fact]
    public void ClearingTheForm_ThrowsAwayFilesThatWereNeverSaved()
    {
        var vm = Vm();
        vm.AttachFile(File_("a.bin", 100));

        vm.ClearFormCommand.Execute(null);

        vm.FormAttachments.Should().BeEmpty();
        vm.AttachmentBytes.Should().Be(0);
    }

    [Fact]
    public void DeletingAnEntry_DeletesItsFiles_AlsoWhenSeveralAreDeletedTogether()
    {
        var vm = Vm();
        vm.AttachFile(File_("a.bin", 100));
        SaveEntry(vm, "One");
        vm.AttachFile(File_("b.bin", 200));
        SaveEntry(vm, "Two");
        vm.AttachmentBytes.Should().Be(300);

        vm.DeleteCommand.Execute(vm.Snapshot().Single(e => e.Title == "One"));
        vm.AttachmentBytes.Should().Be(200);

        vm.SelectAllCommand.Execute(null);
        vm.ConfirmDeleteSelectedCommand.Execute(null);

        vm.AttachmentBytes.Should().Be(0);
    }

    [Fact]
    public void EditingAnEntry_ShowsItsFiles_AndSavingAnotherFileKeepsBoth()
    {
        var vm = Vm();
        vm.AttachFile(File_("a.bin", 100));
        SaveEntry(vm, "Site");
        vm.EditCommand.Execute(vm.Snapshot().Single());

        vm.FormAttachments.Select(r => r.Name).Should().Equal("a.bin");

        vm.AttachFile(File_("b.bin", 50));
        vm.SaveCommand.Execute(null);

        vm.Snapshot().Single().Attachments.Select(a => a.Name).Should().Equal("a.bin", "b.bin");
        vm.AttachmentSnapshot().Should().HaveCount(2);
    }

    [Fact]
    public void SavingAFileOut_WritesTheSameBytes()
    {
        var vm = Vm();
        vm.AttachFile(File_("a.bin", 321, fill: 9));
        SaveEntry(vm, "Site");
        vm.EditCommand.Execute(vm.Snapshot().Single());
        var target = Path.Combine(_folder, "copy.bin");

        vm.SaveAttachmentTo(vm.FormAttachments[0], target).Should().BeTrue();

        File.ReadAllBytes(target).Should().Equal(Enumerable.Repeat((byte)9, 321));
        vm.Status.Should().Contain("not protected");
    }

    [Fact]
    public async Task ThePickers_AreUsed_ForAttachingAndSaving()
    {
        var vm = Vm();
        var source = File_("pick.txt", 10);
        var target = Path.Combine(_folder, "out.txt");
        vm.Dialogs = new Picker(source, target);

        await vm.AddAttachmentCommand.ExecuteAsync(null);
        SaveEntry(vm, "Site");
        vm.EditCommand.Execute(vm.Snapshot().Single());
        await vm.SaveAttachmentCommand.ExecuteAsync(vm.FormAttachments[0]);

        File.Exists(target).Should().BeTrue();
    }

    [Fact]
    public void LoadingAVault_BringsBackTheFiles_AndLoadingAnotherWipesThem()
    {
        var vm = Vm();
        vm.AttachFile(File_("a.bin", 100));
        SaveEntry(vm, "Site");
        var entries = vm.Snapshot();
        var data = vm.AttachmentSnapshot();

        var other = new PasswordsViewModel();
        other.Load(entries, null, data);

        other.AttachmentBytes.Should().Be(100);
        other.Load([]);
        other.AttachmentBytes.Should().Be(0);
    }

    [Fact]
    public void ImportedEntries_KeepTheirFiles_ButOnlyWithinTheLimits()
    {
        var vm = Vm();
        var ok = new PasswordAttachment { Name = "ok.bin", Size = 10 };
        var big = new PasswordAttachment { Name = "big.bin", Size = AttachmentLimits.MaxFileBytes + 1 };
        var missing = new PasswordAttachment { Name = "gone.bin", Size = 5 };
        var entry = new PasswordEntry { Title = "I", Password = "p", Attachments = [ok, big, missing] };
        var data = new Dictionary<Guid, byte[]> { [ok.Id] = new byte[10], [big.Id] = new byte[AttachmentLimits.MaxFileBytes + 1] };

        var dropped = vm.AddRange([entry], data);

        dropped.Should().Be(2);
        vm.Snapshot().Single().Attachments.Should().ContainSingle().Which.Name.Should().Be("ok.bin");
        vm.AttachmentBytes.Should().Be(10);
    }

    private sealed class Picker(string openPath, string savePath) : IFileDialogs
    {
        public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(openPath);

        public Task<string?> SaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult<string?>(savePath);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public void ShowInFolder(string path)
        {
        }
    }

    // ------------------------------------------------------------------ KeePass attachments

    private static string Base64(byte[] bytes, bool gzip = false)
    {
        if (!gzip)
        {
            return Convert.ToBase64String(bytes);
        }

        using var output = new MemoryStream();
        using (var z = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(bytes);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static string Xml(string pool, string entries)
        => $"<KeePassFile><Meta><RecycleBinUUID>BIN=</RecycleBinUUID><Binaries>{pool}</Binaries></Meta><Root><Group><UUID>R=</UUID><Name>DB</Name>{entries}</Group></Root></KeePassFile>";

    private static string Entry(string title, string binaries)
        => $"<Entry><String><Key>Title</Key><Value>{title}</Value></String><String><Key>Password</Key><Value>pw</Value></String>{binaries}</Entry>";

    [Fact]
    public void KeePassXml_AttachmentsFromThePool_AreImported_PlainAndCompressed()
    {
        var plain = Encoding.UTF8.GetBytes("plain file");
        var packed = Encoding.UTF8.GetBytes(new string('z', 5000));
        var xml = Xml(
            $"<Binary ID=\"0\">{Base64(plain)}</Binary><Binary ID=\"1\" Compressed=\"True\">{Base64(packed, gzip: true)}</Binary>",
            Entry("A", "<Binary><Key>one.txt</Key><Value Ref=\"0\"/></Binary><Binary><Key>two.txt</Key><Value Ref=\"1\"/></Binary>"));

        var result = KeePassImporter.ParseXml(xml);

        var entry = result.Passwords.Single();
        entry.Attachments.Select(a => (a.Name, a.Size)).Should().Equal(("one.txt", (long)plain.Length), ("two.txt", (long)packed.Length));
        result.Attachments[entry.Attachments[0].Id].Should().Equal(plain);
        result.Attachments[entry.Attachments[1].Id].Should().Equal(packed);
        result.SkippedAttachments.Should().Be(0);
    }

    [Fact]
    public void KeePassXml_InlineAttachments_AreImported()
    {
        var bytes = Encoding.UTF8.GetBytes("inline");
        var xml = Xml(string.Empty, Entry("A", $"<Binary><Key>in.txt</Key><Value>{Base64(bytes)}</Value></Binary>"));

        var result = KeePassImporter.ParseXml(xml);

        result.Attachments.Values.Should().ContainSingle().Which.Should().Equal(bytes);
    }

    [Fact]
    public void KeePassXml_SkipsOversizeBrokenAndDanglingAttachments_AndSaysSo()
    {
        var big = new byte[AttachmentLimits.MaxFileBytes + 1];
        var bomb = new byte[(int)AttachmentLimits.MaxFileBytes * 3];
        var xml = Xml(
            $"<Binary ID=\"0\">{Base64(big)}</Binary><Binary ID=\"1\">!!!not base64!!!</Binary><Binary ID=\"2\" Compressed=\"True\">{Base64(bomb, gzip: true)}</Binary>",
            Entry("A", "<Binary><Key>big</Key><Value Ref=\"0\"/></Binary><Binary><Key>bad</Key><Value Ref=\"1\"/></Binary>"
                + "<Binary><Key>bomb</Key><Value Ref=\"2\"/></Binary><Binary><Key>dangling</Key><Value Ref=\"99\"/></Binary>"));

        var result = KeePassImporter.ParseXml(xml);

        result.Passwords.Single().Attachments.Should().BeEmpty();
        result.Attachments.Should().BeEmpty();
        result.SkippedAttachments.Should().Be(4);
    }

    [Fact]
    public void KeePassXml_HostileAttachmentNames_AreMadeSafe()
    {
        var xml = Xml(string.Empty, Entry("A", $"<Binary><Key>..\\..\\Windows\\evil.exe</Key><Value>{Base64([1, 2, 3])}</Value></Binary>"));

        KeePassImporter.ParseXml(xml).Passwords.Single().Attachments.Single().Name.Should().Be("evil.exe");
    }

    [Fact]
    public void KeePassXml_RecycleBinFilesAreNotImported()
    {
        var xml = "<KeePassFile><Meta><RecycleBinUUID>BIN=</RecycleBinUUID></Meta><Root><Group><UUID>R=</UUID><Name>DB</Name>"
            + "<Group><UUID>BIN=</UUID><Name>Recycle Bin</Name>"
            + Entry("Deleted", $"<Binary><Key>x</Key><Value>{Base64([1, 2, 3])}</Value></Binary>")
            + "</Group></Group></Root></KeePassFile>";

        var result = KeePassImporter.ParseXml(xml);

        result.Attachments.Should().BeEmpty();
        result.SkippedRecycleBin.Should().Be(1);
    }

    [Fact]
    public void KeePassXml_FilesOnAnEntryWithoutAPassword_AreNotKept()
    {
        var xml = Xml(string.Empty, $"<Entry><String><Key>Title</Key><Value>NoPw</Value></String><Binary><Key>f</Key><Value>{Base64([1])}</Value></Binary></Entry>");

        var result = KeePassImporter.ParseXml(xml);

        result.Passwords.Should().BeEmpty();
        result.Attachments.Should().BeEmpty();
        result.SkippedAttachments.Should().Be(1);
    }

    // ------------------------------------------------------------------ icons

    private static PasswordsViewModel WithFolders()
    {
        var vm = new PasswordsViewModel();
        vm.Load(
            [
                new PasswordEntry { Title = "a", Password = "p", Folder = "Email" },
                new PasswordEntry { Title = "b", Password = "p", Folder = "Email/Work" },
            ]);
        return vm;
    }

    private static void Select(PasswordsViewModel vm, string path) => vm.SelectedNode = vm.FolderNodes.Single(n => n.Path == path);

    private static FolderIcon Icon(string key) => FolderIconCatalog.All.Single(i => i.Key == key);

    [Fact]
    public void TheCatalog_HasUniqueKeys_AndValidShapes_AndTheDefault()
    {
        FolderIconCatalog.All.Select(i => i.Key).Should().OnlyHaveUniqueItems();
        FolderIconCatalog.All.Should().OnlyContain(i => i.PathData.StartsWith("M") && i.Name.Length > 0);
        FolderIconCatalog.IsKnown(FolderIconCatalog.DefaultKey).Should().BeTrue();
        FolderIconCatalog.Get("nonsense").Key.Should().Be(FolderIconCatalog.DefaultKey);
        FolderIconCatalog.All.Should().OnlyContain(i => System.Text.RegularExpressions.Regex.IsMatch(i.PathData, @"^[MmLlHhVvCcSsQqTtAaZz0-9eE.,\s+-]+$"));
    }

    [Fact]
    public void ChoosingAnIcon_ChangesTheFolderLine_AndTheChoices_AndSaves()
    {
        var vm = WithFolders();
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;
        Select(vm, "Email");

        vm.SetFolderIconCommand.Execute(Icon("mail"));

        vm.FolderNodes.Single(n => n.Path == "Email").IconKey.Should().Be("mail");
        vm.FolderNodes.Single(n => n.Path == "Email/Work").IconKey.Should().Be("folder");
        vm.FolderChoices.Single(c => c.Path == "Email").IconKey.Should().Be("mail");
        vm.IconSnapshot().Should().Equal(new Dictionary<string, string> { ["Email"] = "mail" });
        vm.SelectedNode!.Path.Should().Be("Email");
        saves.Should().Be(1);
    }

    [Fact]
    public void PickingTheDefaultIcon_RemovesTheChoice()
    {
        var vm = WithFolders();
        Select(vm, "Email");
        vm.SetFolderIconCommand.Execute(Icon("mail"));

        vm.SetFolderIconCommand.Execute(Icon("folder"));

        vm.IconSnapshot().Should().BeEmpty();
    }

    [Fact]
    public void RenamingAFolder_KeepsItsIconAndThoseBelow()
    {
        var vm = WithFolders();
        Select(vm, "Email");
        vm.SetFolderIconCommand.Execute(Icon("mail"));
        Select(vm, "Email/Work");
        vm.SetFolderIconCommand.Execute(Icon("work"));
        Select(vm, "Email");

        vm.FolderName = "Mail";
        vm.RenameFolderCommand.Execute(null);

        vm.IconSnapshot().Should().Equal(new Dictionary<string, string> { ["Mail"] = "mail", ["Mail/Work"] = "work" });
    }

    [Fact]
    public void DeletingAFolder_DropsItsIcon_ButKeepsTheIconsOfFoldersBelow()
    {
        var vm = WithFolders();
        Select(vm, "Email");
        vm.SetFolderIconCommand.Execute(Icon("mail"));
        Select(vm, "Email/Work");
        vm.SetFolderIconCommand.Execute(Icon("work"));
        Select(vm, "Email");

        vm.DeleteFolderCommand.Execute(null);

        vm.IconSnapshot().Should().Equal(new Dictionary<string, string> { ["Work"] = "work" });
    }

    [Fact]
    public void LoadingIcons_IgnoresUnknownKeysAndBadPaths_AndNothingToSetOnAll()
    {
        var vm = new PasswordsViewModel();

        vm.Load(
            [new PasswordEntry { Title = "a", Password = "p", Folder = "Email" }],
            null,
            null,
            new Dictionary<string, string> { ["Email"] = "mail", ["Other"] = "not-an-icon", ["/"] = "work", ["Gone"] = "star" });

        vm.IconSnapshot().Should().Equal(new Dictionary<string, string> { ["Email"] = "mail" });

        vm.SetFolderIconCommand.Execute(Icon("star"));
        vm.Status.Should().Contain("Choose a folder");
    }
}
