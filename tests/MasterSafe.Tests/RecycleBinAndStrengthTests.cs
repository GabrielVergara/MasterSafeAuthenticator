// File:    RecycleBinAndStrengthTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public sealed class RecycleBinAndStrengthTests
{
    private static PasswordEntry E(string title, string password = "pw", string folder = "") => new() { Title = title, Password = password, Folder = folder };

    private static PasswordsViewModel Vm(params PasswordEntry[] entries)
    {
        var vm = new PasswordsViewModel();
        vm.Load(entries);
        return vm;
    }

    private static void OpenBin(PasswordsViewModel vm) => vm.SelectedNode = vm.FolderNodes.Single(n => n.IsBin);

    // ------------------------------------------------------------------ recycle bin

    [Fact]
    public void DeletingAnEntry_MovesItToTheBin_WhereItIsListedAndCounted()
    {
        var vm = Vm(E("A"), E("B"));
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;

        vm.DeleteCommand.Execute(vm.Entries[0]);

        vm.ActiveSnapshot().Should().ContainSingle().Which.Title.Should().Be("B");
        vm.Snapshot().Should().HaveCount(2, "it is still saved, in the bin");
        vm.BinCount.Should().Be(1);
        vm.FolderNodes.Single(n => n.IsBin).Count.Should().Be(1);
        vm.FolderNodes.Single(n => n.Path is null && !n.IsBin).Count.Should().Be(1);
        saves.Should().Be(1);
        vm.Status.Should().Contain("recycle bin");

        OpenBin(vm);

        vm.IsInBin.Should().BeTrue();
        vm.Entries.Should().ContainSingle().Which.Title.Should().Be("A");
        vm.Rows.Single().IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void RestoringBringsTheEntryBack_ToItsFolder()
    {
        var vm = Vm(E("A", folder: "Email"));
        vm.DeleteCommand.Execute(vm.Entries[0]);
        OpenBin(vm);

        vm.RestoreCommand.Execute(vm.Entries[0]);

        vm.BinCount.Should().Be(0);
        vm.ActiveSnapshot().Single().Folder.Should().Be("Email");
        vm.FolderNodes.Should().Contain(n => n.Path == "Email");
        vm.Status.Should().Contain("Restored A to Email");
    }

    [Fact]
    public void DeletingFromTheBin_IsForGood_AfterAQuestionThatSaysSo()
    {
        var vm = Vm(E("A"));
        vm.DeleteCommand.Execute(vm.Entries[0]);
        OpenBin(vm);

        vm.RequestDeleteCommand.Execute(vm.Entries[0]);
        vm.DeleteQuestion.Should().Contain("for good");
        vm.ConfirmDeleteCommand.Execute(null);

        vm.Snapshot().Should().BeEmpty();
        vm.Status.Should().Contain("for good");
    }

    [Fact]
    public void AnEntryFromTheList_IsAskedAboutTheBin_NotAboutDeletingForGood()
    {
        var vm = Vm(E("A"));

        vm.RequestDeleteCommand.Execute(vm.Entries[0]);

        vm.DeleteQuestion.Should().Contain("recycle bin").And.Contain("30 days");
        vm.DeleteConfirmText.Should().Contain("recycle bin");
    }

    [Fact]
    public void EmptyingTheBin_NeedsConfirmation_AndRemovesOnlyDeletedEntries()
    {
        var vm = Vm(E("keep"), E("x"), E("y"));
        vm.DeleteCommand.Execute(vm.Entries.Single(e => e.Title == "x"));
        vm.DeleteCommand.Execute(vm.Entries.Single(e => e.Title == "y"));
        OpenBin(vm);

        vm.RequestEmptyBinCommand.Execute(null);
        vm.IsConfirmingEmptyBin.Should().BeTrue();
        vm.CancelEmptyBinCommand.Execute(null);
        vm.BinCount.Should().Be(2);

        vm.RequestEmptyBinCommand.Execute(null);
        vm.ConfirmEmptyBinCommand.Execute(null);

        vm.Snapshot().Select(e => e.Title).Should().Equal("keep");
        vm.Status.Should().Contain("Deleted 2 passwords for good");
    }

    [Fact]
    public void BulkDeleteInTheBin_IsForGood_AndMovesToTheBinElsewhere()
    {
        var vm = Vm(E("a"), E("b"));
        vm.SelectAllCommand.Execute(null);
        vm.RequestDeleteSelectedCommand.Execute(null);
        vm.BulkDeleteQuestion.Should().Contain("recycle bin");
        vm.ConfirmDeleteSelectedCommand.Execute(null);
        vm.BinCount.Should().Be(2);

        OpenBin(vm);
        vm.SelectAllCommand.Execute(null);
        vm.RequestDeleteSelectedCommand.Execute(null);
        vm.BulkDeleteQuestion.Should().Contain("for good");
        vm.ConfirmDeleteSelectedCommand.Execute(null);

        vm.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void EntriesInTheBinForMoreThanThirtyDays_AreRemovedWhenTheVaultOpens()
    {
        var old = E("old").WithDeleted(DateTimeOffset.UtcNow.AddDays(-31));
        var recent = E("recent").WithDeleted(DateTimeOffset.UtcNow.AddDays(-29));

        var vm = Vm(old, recent, E("live"));

        vm.Snapshot().Select(e => e.Title).Should().BeEquivalentTo("recent", "live");
    }

    [Fact]
    public void TheBinSurvivesSavingAndLoading()
    {
        var deleted = E("gone").WithDeleted(DateTimeOffset.UtcNow);
        var contents = new MasterSafe.Crypto.VaultContents { Passwords = [deleted, E("here")] };

        var decoded = MasterSafe.Crypto.VaultContentsCodec.Decode(MasterSafe.Crypto.VaultContentsCodec.Encode(contents));

        decoded.Passwords.Single(p => p.Title == "gone").IsDeleted.Should().BeTrue();
        decoded.Passwords.Single(p => p.Title == "here").IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void DeletedEntries_AreNotSearchedInTheNormalLists_OrCountedInFolders()
    {
        var vm = Vm(E("alpha", folder: "X"), E("alpha two", folder: "X"));
        vm.DeleteCommand.Execute(vm.Entries[0]);

        vm.SearchText = "alpha";

        vm.Entries.Should().ContainSingle();
        vm.FolderNodes.Single(n => n.Path == "X").Count.Should().Be(1);
    }

    // ------------------------------------------------------------------ history

    [Fact]
    public void ChangingAPassword_KeepsTheOldOneInTheHistory()
    {
        var vm = Vm(E("Site", "old-password"));
        vm.EditCommand.Execute(vm.Entries[0]);

        vm.Password = "new-password-123";
        vm.SaveCommand.Execute(null);

        var saved = vm.ActiveSnapshot().Single();
        saved.Password.Should().Be("new-password-123");
        saved.History.Should().ContainSingle().Which.Password.Should().Be("old-password");
    }

    [Fact]
    public void SavingWithoutChanges_AddsNoHistory()
    {
        var vm = Vm(E("Site", "pw"));
        vm.EditCommand.Execute(vm.Entries[0]);

        vm.SaveCommand.Execute(null);

        vm.ActiveSnapshot().Single().History.Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheLatestTenVersionsAreKept_NewestFirst()
    {
        var vm = Vm(E("Site", "p0"));
        for (var i = 1; i <= 14; i++)
        {
            vm.EditCommand.Execute(vm.ActiveSnapshot().Single());
            vm.Password = "p" + i;
            vm.SaveCommand.Execute(null);
        }

        var history = vm.ActiveSnapshot().Single().History;

        history.Should().HaveCount(PasswordVersion.MaxKept);
        history[0].Password.Should().Be("p13");
        history[^1].Password.Should().Be("p4");
    }

    [Fact]
    public void TheFormListsEarlierVersions_AndCanLoadOneBack()
    {
        var vm = Vm(E("Site", "first"));
        vm.EditCommand.Execute(vm.Entries[0]);
        vm.Password = "second";
        vm.SaveCommand.Execute(null);
        vm.EditCommand.Execute(vm.ActiveSnapshot().Single());
        vm.HasHistory.Should().BeTrue();

        vm.UseVersionCommand.Execute(vm.FormHistory[0]);

        vm.Password.Should().Be("first");
        vm.Status.Should().Contain("Choose Save");
        vm.ActiveSnapshot().Single().Password.Should().Be("second", "nothing changes until Save");
    }

    [Fact]
    public void CopyingAnOldPassword_AsksTheViewToCopyIt()
    {
        var vm = Vm(E("Site", "first"));
        vm.EditCommand.Execute(vm.Entries[0]);
        vm.Password = "second";
        vm.SaveCommand.Execute(null);
        vm.EditCommand.Execute(vm.ActiveSnapshot().Single());
        string? copied = null;
        vm.CopyRequested += (_, text) => copied = text;

        vm.CopyVersionPasswordCommand.Execute(vm.FormHistory[0]);

        copied.Should().Be("first");
    }

    [Fact]
    public void ClearingTheForm_HidesTheHistory()
    {
        var vm = Vm(E("Site", "first"));
        vm.EditCommand.Execute(vm.Entries[0]);
        vm.Password = "second";
        vm.SaveCommand.Execute(null);
        vm.EditCommand.Execute(vm.ActiveSnapshot().Single());

        vm.ClearFormCommand.Execute(null);

        vm.HasHistory.Should().BeFalse();
    }

    // ------------------------------------------------------------------ strength

    [Theory]
    [InlineData("", 0)]
    [InlineData("password", 0)]
    [InlineData("Password1", 0)]
    [InlineData("123456", 0)]
    [InlineData("abcdefgh", 0)]
    [InlineData("aaaaaaaaaaaa", 0)]
    public void KnownBadOrModestPasswords_ScoreLow(string password, int maxScore)
        => PasswordStrength.Evaluate(password).Score.Should().BeLessThanOrEqualTo(maxScore);

    [Theory]
    [InlineData("k7Q!f2$Mx9tD@w3H")]
    [InlineData("correct horse battery staple")]
    [InlineData("K7QF2-M9XTD-W3HPA-8RCNB-4ZJYE-6VGUS")]
    public void LongRandomOrWordBasedPasswords_ScoreHigh(string password)
        => PasswordStrength.Evaluate(password).Score.Should().BeGreaterThanOrEqualTo(3);

    [Fact]
    public void LongerIsAlwaysAtLeastAsStrong_ForTheSameKind()
    {
        var scores = new[] { "k7Q!f2", "k7Q!f2$M", "k7Q!f2$Mx9tD", "k7Q!f2$Mx9tD@w3H" }.Select(p => PasswordStrength.Evaluate(p).Bits).ToList();

        scores.Should().BeInAscendingOrder();
    }

    [Fact]
    public void ARepeatedOrSequentialPassword_IsWeakerThanARandomOneOfTheSameLength()
    {
        PasswordStrength.Evaluate("abcdefghijkl").Bits.Should().BeLessThan(PasswordStrength.Evaluate("kq7fz2mxt9dw").Bits);
        PasswordStrength.Evaluate("aaaaaaaaaaaa").Bits.Should().BeLessThan(PasswordStrength.Evaluate("kq7fz2mxt9dw").Bits);
    }

    [Fact]
    public void UsingTheTitleOrUserName_LowersTheScoreAndSaysSo()
    {
        var plain = PasswordStrength.Evaluate("MyGitHub2024!x");
        var related = PasswordStrength.Evaluate("MyGitHub2024!x", "GitHub");

        related.Bits.Should().BeLessThan(plain.Bits);
        related.Hint.Should().Contain("title or user name");
    }

    [Fact]
    public void TheFormShowsTheMeterAsThePasswordIsTyped()
    {
        var vm = new PasswordsViewModel();

        vm.Password = "password";
        vm.StrengthScore.Should().Be(0);
        vm.StrengthText.Should().Contain("Very weak");

        vm.Password = "K7QF2-M9XTD-W3HPA-8RCNB";
        vm.StrengthScore.Should().BeGreaterThanOrEqualTo(3);

        vm.Password = string.Empty;
        vm.StrengthText.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ health

    [Fact]
    public void TheHealthReport_FindsWeakReusedAndOldPasswords_AndIgnoresTheBin()
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            E("Weak", "123456"),
            E("Strong1", "K7QF2-M9XTD-W3HPA-8RCNB"),
            new PasswordEntry { Title = "Old", Password = "k7Q!f2$Mx9tD@w3H", ModifiedUtc = now.AddDays(-500) },
            new PasswordEntry { Title = "Reuse A", Password = "Zx9!kQ72mWp@4tLc", ModifiedUtc = now },
            new PasswordEntry { Title = "Reuse B", Password = "Zx9!kQ72mWp@4tLc", ModifiedUtc = now },
            E("Deleted", "123456").WithDeleted(now),
        }.Select(e => e.ModifiedUtc == default ? e : e).ToList();

        var report = PasswordHealth.Analyze(entries.Select(e => e.Title == "Old" || e.Title.StartsWith("Reuse") ? e : new PasswordEntry
        {
            Id = e.Id, Title = e.Title, Password = e.Password, DeletedUtc = e.DeletedUtc, ModifiedUtc = now,
        }), now);

        report.Weak.Select(i => i.Entry.Title).Should().Equal("Weak");
        report.Reused.Select(i => i.Entry.Title).Should().BeEquivalentTo("Reuse A", "Reuse B");
        report.Old.Select(i => i.Entry.Title).Should().Equal("Old");
        report.Checked.Should().Be(5);
        report.IsClean.Should().BeFalse();
        report.EntriesNeedingAttention.Should().Be(4);
    }

    [Fact]
    public void BreachCounts_AreListedWhenGiven()
    {
        var a = E("A", "K7QF2-M9XTD-W3HPA-8RCNB");
        var now = DateTimeOffset.UtcNow;

        var without = PasswordHealth.Analyze([a], now);
        var with = PasswordHealth.Analyze([a], now, new Dictionary<Guid, int> { [a.Id] = 1234 });

        without.Breached.Should().BeEmpty();
        with.Breached.Should().ContainSingle().Which.Reason.Should().Contain("1,234");
    }

    [Fact]
    public void AHealthyVault_IsClean()
    {
        var now = DateTimeOffset.UtcNow;

        var report = PasswordHealth.Analyze([new PasswordEntry { Title = "A", Password = "K7QF2-M9XTD-W3HPA-8RCNB", ModifiedUtc = now }], now);

        report.IsClean.Should().BeTrue();
    }
}
