// File:    PasswordUsabilityTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public sealed class PasswordUsabilityTests
{
    private static PasswordEntry E(string title, string folder = "", string url = "", string user = "u", DateTimeOffset? modified = null)
        => new() { Title = title, Password = "pw-" + title, Folder = folder, Url = url, UserName = user, ModifiedUtc = modified ?? DateTimeOffset.UtcNow };

    private static PasswordsViewModel Vm(params PasswordEntry[] entries)
    {
        var vm = new PasswordsViewModel();
        vm.Load(entries);
        return vm;
    }

    // ------------------------------------------------------------------ sorting

    [Fact]
    public void TheListCanBeOrderedFourWays()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = Vm(E("banana", "B", modified: now.AddDays(-3)), E("Apple", "A", modified: now.AddDays(-1)), E("cherry", "A", modified: now.AddDays(-2)));

        vm.Entries.Select(e => e.Title).Should().Equal("Apple", "banana", "cherry");

        vm.SelectedSort = PasswordsViewModel.SortOptions.Single(o => o.Key == "title-desc");
        vm.Entries.Select(e => e.Title).Should().Equal("cherry", "banana", "Apple");

        vm.SelectedSort = PasswordsViewModel.SortOptions.Single(o => o.Key == "changed");
        vm.Entries.Select(e => e.Title).Should().Equal("Apple", "cherry", "banana");

        vm.SelectedSort = PasswordsViewModel.SortOptions.Single(o => o.Key == "folder");
        vm.Entries.Select(e => e.Title).Should().Equal("Apple", "cherry", "banana");
    }

    [Fact]
    public void ChoosingAnOrder_IsAnnounced_AndARestoredOrderIsNot()
    {
        var vm = Vm(E("a"), E("b"));
        var raised = 0;
        vm.SortChanged += (_, _) => raised++;

        vm.SelectedSort = PasswordsViewModel.SortOptions[2];
        raised.Should().Be(1);

        vm.RestoreSort("title-desc");
        vm.SelectedSort.Key.Should().Be("title-desc");
        raised.Should().Be(1);

        vm.RestoreSort("nonsense");
        vm.SelectedSort.Key.Should().Be("title");
    }

    // ------------------------------------------------------------------ opening a site

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://example.com/login?x=1", "https://example.com/login?x=1")]
    [InlineData("  http://example.com  ", "http://example.com/")]
    [InlineData("example.com:8443/path", "https://example.com:8443/path")]
    public void NormalAddresses_AreOpenable(string input, string expected)
    {
        PasswordsViewModel.TryNormalizeUrl(input, out var url).Should().BeTrue();
        url.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/windows/system32/cmd.exe")]
    [InlineData("ftp://example.com")]
    [InlineData("c:\\windows\\system32\\cmd.exe")]
    [InlineData("cmd /c calc")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://")]
    [InlineData("data:text/html,<script>")]
    [InlineData("ms-settings:privacy")]
    public void AnythingElse_IsRefused(string input)
        => PasswordsViewModel.TryNormalizeUrl(input, out _).Should().BeFalse();

    [Fact]
    public void ControlCharactersAndHugeAddresses_AreRefused()
    {
        PasswordsViewModel.TryNormalizeUrl("https://example.com/\r\nx", out _).Should().BeFalse();
        PasswordsViewModel.TryNormalizeUrl("https://example.com/" + new string('a', 3000), out _).Should().BeFalse();
    }

    [Fact]
    public void OpenSite_UsesTheBrowserOnlyForSafeAddresses()
    {
        var vm = Vm(E("Good", url: "example.com"), E("Bad", url: "javascript:alert(1)"), E("None"), E("Plain", url: "http://example.com"));
        var opened = new List<string>();
        vm.UrlOpener = opened.Add;

        vm.OpenUrlCommand.Execute(vm.Snapshot().Single(e => e.Title == "Good"));
        vm.Status.Should().Be("Opened https://example.com/.");

        vm.OpenUrlCommand.Execute(vm.Snapshot().Single(e => e.Title == "Bad"));
        vm.Status.Should().Contain("not a web address");

        vm.OpenUrlCommand.Execute(vm.Snapshot().Single(e => e.Title == "None"));
        vm.Status.Should().Contain("no web address");

        vm.OpenUrlCommand.Execute(vm.Snapshot().Single(e => e.Title == "Plain"));
        vm.Status.Should().Contain("not encrypted");

        opened.Should().Equal("https://example.com/", "http://example.com/");
    }

    // ------------------------------------------------------------------ the current entry and shortcuts

    [Fact]
    public void ShortcutsActOnTheCurrentEntry_WhichIsTheClickedOneOrTheOnlyOne()
    {
        var vm = Vm(E("One", user: "alice"), E("Two", user: "bob"));
        var copied = new List<string>();
        vm.CopyRequested += (_, text) => copied.Add(text);

        vm.CopyCurrentUserCommand.Execute(null);
        copied.Should().BeEmpty("with two entries and none chosen, nothing is guessed");

        vm.SetCurrentCommand.Execute(vm.Snapshot().Single(e => e.Title == "Two"));
        vm.CopyCurrentUserCommand.Execute(null);
        vm.CopyCurrentPasswordCommand.Execute(null);

        copied.Should().Equal("bob", "pw-Two");
        vm.Rows.Single(r => r.IsCurrent).Entry.Title.Should().Be("Two");
    }

    [Fact]
    public void WithOneEntryShown_ItIsTheCurrentOne()
    {
        var vm = Vm(E("Only", user: "solo"));
        string? copied = null;
        vm.CopyRequested += (_, text) => copied = text;

        vm.CopyCurrentUserCommand.Execute(null);

        copied.Should().Be("solo");
    }

    [Fact]
    public void EditingAnEntry_MakesItCurrent_AndTheMarkSurvivesRefreshes()
    {
        var vm = Vm(E("One"), E("Two"));
        vm.EditCommand.Execute(vm.Snapshot().Single(e => e.Title == "Two"));

        vm.SearchText = "T";

        vm.Rows.Single(r => r.IsCurrent).Entry.Title.Should().Be("Two");
    }

    [Fact]
    public void DeleteShortcut_AsksFirst_AndEntriesInTheBinAreNotCopied()
    {
        var vm = Vm(E("One"));
        vm.SetCurrentCommand.Execute(vm.Snapshot()[0]);

        vm.DeleteCurrentCommand.Execute(null);

        vm.HasPendingDelete.Should().BeTrue("a shortcut never deletes without asking");
        vm.ConfirmDeleteCommand.Execute(null);
        vm.SelectedNode = vm.FolderNodes.Single(n => n.IsBin);
        var copied = false;
        vm.CopyRequested += (_, _) => copied = true;

        vm.SetCurrentCommand.Execute(vm.Snapshot()[0]);
        vm.CopyCurrentPasswordCommand.Execute(null);

        copied.Should().BeFalse();
    }

    [Fact]
    public void NewEntryAndSearchShortcuts_AskTheViewToMoveTheFocus()
    {
        var vm = Vm(E("One"));
        vm.EditCommand.Execute(vm.Snapshot()[0]);
        var title = 0;
        var search = 0;
        vm.FocusTitleRequested += (_, _) => title++;
        vm.FocusSearchRequested += (_, _) => search++;

        vm.NewEntryCommand.Execute(null);
        vm.FocusSearchCommand.Execute(null);

        title.Should().Be(1);
        search.Should().Be(1);
        vm.Title.Should().BeEmpty("the form is empty for the new entry");
    }

    // ------------------------------------------------------------------ dragging

    private static FolderNode Node(PasswordsViewModel vm, string path) => vm.FolderNodes.Single(n => n.Path == path && !n.IsBin);

    [Fact]
    public void DraggingAnEntryOntoAFolder_MovesIt()
    {
        var vm = Vm(E("a"), E("b", "Bank"));
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;

        var moved = vm.MoveDraggedTo(vm.Snapshot().Single(e => e.Title == "a").Id, Node(vm, "Bank"));

        moved.Should().Be(1);
        vm.Snapshot().Single(e => e.Title == "a").Folder.Should().Be("Bank");
        vm.Status.Should().Contain("Moved 1 password to Bank");
        saves.Should().Be(1);
    }

    [Fact]
    public void DraggingATickedEntry_MovesAllTickedEntries()
    {
        var vm = Vm(E("a"), E("b"), E("c"), E("Bank"));
        vm.Rows.Single(r => r.Entry.Title == "a").IsSelected = true;
        vm.Rows.Single(r => r.Entry.Title == "b").IsSelected = true;
        var folder = vm.FolderNodes.Count;
        vm.FolderName = "Target";
        vm.CreateFolderCommand.Execute(null);
        vm.SelectedNode = vm.FolderNodes.Single(n => n.Path is null && !n.IsBin);
        vm.Rows.Single(r => r.Entry.Title == "a").IsSelected = true;
        vm.Rows.Single(r => r.Entry.Title == "b").IsSelected = true;

        var moved = vm.MoveDraggedTo(vm.Snapshot().Single(e => e.Title == "a").Id, Node(vm, "Target"));

        moved.Should().Be(2);
        vm.Snapshot().Where(e => e.Folder == "Target").Select(e => e.Title).Should().BeEquivalentTo("a", "b");
        folder.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DraggingAnUntickedEntry_MovesOnlyThatOne_EvenWhenOthersAreTicked()
    {
        var vm = Vm(E("a"), E("b"), E("Bank", "Bank"));
        vm.Rows.Single(r => r.Entry.Title == "b").IsSelected = true;

        vm.MoveDraggedTo(vm.Snapshot().Single(e => e.Title == "a").Id, Node(vm, "Bank"));

        vm.Snapshot().Single(e => e.Title == "a").Folder.Should().Be("Bank");
        vm.Snapshot().Single(e => e.Title == "b").Folder.Should().BeEmpty();
    }

    [Fact]
    public void DroppingOnTheRecycleBin_DeletesRestorably()
    {
        var vm = Vm(E("a"));

        vm.MoveDraggedTo(vm.Snapshot()[0].Id, vm.FolderNodes.Single(n => n.IsBin)).Should().Be(1);

        vm.ActiveSnapshot().Should().BeEmpty();
        vm.BinCount.Should().Be(1);
    }

    [Fact]
    public void DroppingOnAllPasswords_ExplainsInsteadOfMoving()
    {
        var vm = Vm(E("a", "Bank"));

        vm.MoveDraggedTo(vm.Snapshot()[0].Id, vm.FolderNodes.Single(n => n.Path is null && !n.IsBin)).Should().Be(0);

        vm.Snapshot()[0].Folder.Should().Be("Bank");
        vm.Status.Should().Contain("Drop the password on a folder");
    }

    [Fact]
    public void DroppingWhereItAlreadyIs_OrOnNothing_ChangesNothing()
    {
        var vm = Vm(E("a", "Bank"));
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;

        vm.MoveDraggedTo(vm.Snapshot()[0].Id, Node(vm, "Bank")).Should().Be(0);
        vm.MoveDraggedTo(vm.Snapshot()[0].Id, null).Should().Be(0);
        vm.MoveDraggedTo(Guid.NewGuid(), Node(vm, "Bank")).Should().Be(0);

        saves.Should().Be(0);
    }
}
