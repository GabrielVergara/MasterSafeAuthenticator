// File:    PasswordFolderTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public sealed class PasswordFolderTests
{
    private static PasswordEntry Entry(string title, string folder = "")
        => new() { Title = title, Password = "pw-" + title, Folder = folder };

    private static PasswordsViewModel Vm(params PasswordEntry[] entries)
    {
        var vm = new PasswordsViewModel();
        vm.Load(entries);
        return vm;
    }

    private static void Select(PasswordsViewModel vm, string? path)
        => vm.SelectedNode = vm.FolderNodes.Single(n => n.Path == path && !n.IsBin);

    [Theory]
    [InlineData("  Email / Work  ", "Email/Work")]
    [InlineData("A\\B\\C", "A/B/C")]
    [InlineData("//A///B//", "A/B")]
    [InlineData("../secret/./x", "secret/x")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void FolderPath_CleansPaths(string? input, string expected)
        => FolderPath.Normalize(input).Should().Be(expected);

    [Fact]
    public void FolderPath_LimitsDepthAndNameLength()
    {
        FolderPath.Normalize(string.Join('/', Enumerable.Range(0, 20).Select(i => "f" + i)))
            .Split('/').Should().HaveCount(FolderPath.MaxDepth);
        FolderPath.Normalize(new string('x', 500)).Should().HaveLength(FolderPath.MaxNameLength);
    }

    [Fact]
    public void FolderPath_Helpers()
    {
        FolderPath.Parent("A/B/C").Should().Be("A/B");
        FolderPath.Parent("A").Should().BeEmpty();
        FolderPath.Name("A/B/C").Should().Be("C");
        FolderPath.Depth("A/B").Should().Be(2);
        FolderPath.IsInside("A/B", "A").Should().BeTrue();
        FolderPath.IsInside("AB", "A").Should().BeFalse("only whole folder names count");
        FolderPath.IsInside("a/b", "A").Should().BeTrue();
        FolderPath.IsInside("X", string.Empty).Should().BeTrue();
        FolderPath.WithAncestors("A/B/C").Should().Equal("A", "A/B", "A/B/C");
    }

    [Fact]
    public void TheFolderListIsBuiltFromTheEntries_WithCounts_AndParentsOfDeepFolders()
    {
        var vm = Vm(Entry("a", "Email/Work"), Entry("b", "Email"), Entry("c"));

        vm.FolderNodes.Where(n => !n.IsBin).Select(n => (n.Path, n.Count)).Should().Equal(
            ((string?)null, 3), ("Email", 2), ("Email/Work", 1));
        vm.FolderChoices.Select(c => c.Path).Should().Equal(string.Empty, "Email", "Email/Work");
    }

    [Fact]
    public void ChoosingAFolder_ShowsItsEntriesAndThoseBelow()
    {
        var vm = Vm(Entry("a", "Email/Work"), Entry("b", "Email"), Entry("c"), Entry("d", "Bank"));

        Select(vm, "Email");

        vm.Entries.Select(e => e.Title).Should().Equal("a", "b");

        Select(vm, null);

        vm.Entries.Should().HaveCount(4);
    }

    [Fact]
    public void SearchWorksInsideTheSelectedFolder()
    {
        var vm = Vm(Entry("alpha", "X"), Entry("beta", "X"), Entry("alpha2", "Y"));
        Select(vm, "X");

        vm.SearchText = "alpha";

        vm.Entries.Select(e => e.Title).Should().Equal("alpha");
    }

    [Fact]
    public void CreateFolder_AddsAnEmptyFolder_UnderTheSelectedOne_AndSaves()
    {
        var vm = Vm(Entry("a", "Email"));
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;
        Select(vm, "Email");

        vm.FolderName = "Work";
        vm.CreateFolderCommand.Execute(null);

        vm.FolderNodes.Select(n => n.Path).Should().Contain("Email/Work");
        vm.SelectedNode!.Path.Should().Be("Email/Work");
        vm.FolderSnapshot().Should().Equal("Email/Work");
        vm.FolderName.Should().BeEmpty();
        saves.Should().Be(1);
    }

    [Fact]
    public void CreateFolder_RefusesEmptyAndDuplicateNames()
    {
        var vm = Vm(Entry("a", "Email"));

        vm.CreateFolderCommand.Execute(null);
        vm.Status.Should().Contain("name");

        vm.FolderName = "email";
        vm.CreateFolderCommand.Execute(null);
        vm.Status.Should().Contain("already exists");
        vm.FolderSnapshot().Should().BeEmpty();
    }

    [Fact]
    public void NewEntries_GoIntoTheSelectedFolder_ByDefault_AndCanBeChanged()
    {
        var vm = Vm(Entry("a", "Email"), Entry("b", "Bank"));
        Select(vm, "Email");

        vm.Title = "New";
        vm.Password = "p";
        vm.SaveCommand.Execute(null);

        vm.Snapshot().Single(e => e.Title == "New").Folder.Should().Be("Email");

        vm.Title = "Other";
        vm.Password = "p";
        vm.FormFolder = vm.FolderChoices.Single(c => c.Path == "Bank");
        vm.SaveCommand.Execute(null);

        vm.Snapshot().Single(e => e.Title == "Other").Folder.Should().Be("Bank");
    }

    [Fact]
    public void EditingAnEntry_ShowsItsFolder_AndSavingKeepsOrChangesIt()
    {
        var vm = Vm(Entry("a", "Email"), Entry("b", "Bank"));
        vm.EditCommand.Execute(vm.Snapshot().Single(e => e.Title == "a"));

        vm.FormFolder!.Path.Should().Be("Email");

        vm.FormFolder = vm.FolderChoices.Single(c => c.Path == "Bank");
        vm.SaveCommand.Execute(null);

        vm.Snapshot().Single(e => e.Title == "a").Folder.Should().Be("Bank");
    }

    [Fact]
    public void RenameFolder_MovesEntriesAndSubfolders()
    {
        var vm = Vm(Entry("a", "Email/Work"), Entry("b", "Email"), Entry("c", "Other"));
        Select(vm, "Email");

        vm.FolderName = "Mail";
        vm.RenameFolderCommand.Execute(null);

        vm.Snapshot().ToDictionary(e => e.Title, e => e.Folder).Should().Equal(
            new Dictionary<string, string> { ["a"] = "Mail/Work", ["b"] = "Mail", ["c"] = "Other" });
        vm.SelectedNode!.Path.Should().Be("Mail");
    }

    [Fact]
    public void RenameFolder_RefusesANameThatExists()
    {
        var vm = Vm(Entry("a", "Email"), Entry("b", "Bank"));
        Select(vm, "Email");

        vm.FolderName = "Bank";
        vm.RenameFolderCommand.Execute(null);

        vm.Status.Should().Contain("already exists");
        vm.Snapshot().Single(e => e.Title == "a").Folder.Should().Be("Email");
    }

    [Fact]
    public void DeleteFolder_KeepsEveryPassword_MovingThemUp()
    {
        var vm = Vm(Entry("a", "Email/Work"), Entry("b", "Email"), Entry("c", "Email/Work/Deep"));
        Select(vm, "Email/Work");

        vm.DeleteFolderCommand.Execute(null);

        vm.Snapshot().Should().HaveCount(3);
        vm.Snapshot().ToDictionary(e => e.Title, e => e.Folder).Should().Equal(
            new Dictionary<string, string> { ["a"] = "Email", ["b"] = "Email", ["c"] = "Email/Deep" });
        vm.Status.Should().Contain("2 passwords were moved");
        vm.SelectedNode!.Path.Should().Be("Email");
    }

    [Fact]
    public void DeleteFolder_AtTheTop_MovesEntriesToTheTopLevel_AndEmptyFoldersGoAway()
    {
        var vm = Vm(Entry("a", "Email"));
        vm.FolderName = "Empty";
        vm.CreateFolderCommand.Execute(null);

        vm.DeleteFolderCommand.Execute(null);
        vm.FolderSnapshot().Should().BeEmpty();

        Select(vm, "Email");
        vm.DeleteFolderCommand.Execute(null);

        vm.Snapshot().Single().Folder.Should().BeEmpty();
        vm.SelectedNode!.Path.Should().BeNull();
    }

    [Fact]
    public void MoveSelected_MovesTickedEntries_AndSavesOnce()
    {
        var vm = Vm(Entry("a"), Entry("b"), Entry("c", "Bank"));
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;
        vm.Rows.Single(r => r.Entry.Title == "a").IsSelected = true;
        vm.Rows.Single(r => r.Entry.Title == "b").IsSelected = true;

        vm.MoveTarget = vm.FolderChoices.Single(c => c.Path == "Bank");
        vm.MoveSelectedCommand.Execute(null);

        vm.Snapshot().Should().OnlyContain(e => e.Folder == "Bank");
        vm.SelectedCount.Should().Be(0);
        vm.Status.Should().Contain("Moved 2 passwords to Bank");
        saves.Should().Be(1);
    }

    [Fact]
    public void ChangingTheFolder_ClearsTicks_SoNothingOutOfSightIsDeleted()
    {
        var vm = Vm(Entry("a", "X"), Entry("b", "Y"));
        vm.Rows.Single(r => r.Entry.Title == "a").IsSelected = true;

        Select(vm, "Y");

        vm.SelectedCount.Should().Be(0);
    }

    [Fact]
    public void LoadingAnUntrustedFolder_CleansItUp()
    {
        var vm = new PasswordsViewModel();

        vm.Load([Entry("a", "..\\..//evil/./x")], [" Empty / One ", string.Empty]);

        vm.Snapshot().Single().Folder.Should().Be("evil/x");
        vm.FolderSnapshot().Should().Equal("Empty/One");
    }

    [Fact]
    public void ReloadingTheVault_ResetsToAllPasswords()
    {
        var vm = Vm(Entry("a", "X"));
        Select(vm, "X");

        vm.Load([Entry("b", "Z")]);

        vm.SelectedNode!.Path.Should().BeNull();
        vm.Entries.Should().ContainSingle();
    }
}
