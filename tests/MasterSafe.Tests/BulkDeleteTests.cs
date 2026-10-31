// File:    BulkDeleteTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using MasterSafe.Core;
using Xunit;

namespace MasterSafe.Tests;

public sealed class BulkDeleteTests
{
    private static PasswordsViewModel WithEntries(params string[] titles)
    {
        var vm = new PasswordsViewModel();
        vm.Load(titles.Select(t => new PasswordEntry { Title = t, Password = "pw-" + t }));
        return vm;
    }

    [Fact]
    public void TickingRows_CountsThem_AndNothingIsDeletedYet()
    {
        var vm = WithEntries("A", "B", "C");

        vm.Rows[0].IsSelected = true;
        vm.Rows[2].IsSelected = true;

        vm.SelectedCount.Should().Be(2);
        vm.HasSelection.Should().BeTrue();
        vm.Snapshot().Should().HaveCount(3);
    }

    [Fact]
    public void DeleteSelected_AsksFirst_ThenRemovesOnlyTheTickedOnes_AndSavesOnce()
    {
        var vm = WithEntries("A", "B", "C");
        var saves = 0;
        vm.EntriesChanged += (_, _) => saves++;
        vm.Rows[0].IsSelected = true;
        vm.Rows[1].IsSelected = true;

        vm.RequestDeleteSelectedCommand.Execute(null);

        vm.IsConfirmingBulkDelete.Should().BeTrue();
        vm.BulkDeleteQuestion.Should().Contain("2 selected passwords");
        vm.Snapshot().Should().HaveCount(3);

        vm.ConfirmDeleteSelectedCommand.Execute(null);

        vm.Snapshot().Select(e => e.Title).Should().Equal("C");
        vm.Rows.Should().ContainSingle();
        vm.SelectedCount.Should().Be(0);
        vm.IsConfirmingBulkDelete.Should().BeFalse();
        vm.Status.Should().Be("Deleted 2 passwords.");
        saves.Should().Be(1);
    }

    [Fact]
    public void CancelKeepsEverything()
    {
        var vm = WithEntries("A", "B");
        vm.SelectAllCommand.Execute(null);
        vm.RequestDeleteSelectedCommand.Execute(null);

        vm.CancelDeleteSelectedCommand.Execute(null);

        vm.IsConfirmingBulkDelete.Should().BeFalse();
        vm.Snapshot().Should().HaveCount(2);
        vm.SelectedCount.Should().Be(2, "the ticks stay so the user can change their mind");
    }

    [Fact]
    public void SelectAll_OnlyTicksWhatTheSearchShows_AndASearchClearsTicksOnHiddenRows()
    {
        var vm = WithEntries("Apple", "Banana", "Avocado");
        vm.Rows.Single(r => r.Entry.Title == "Banana").IsSelected = true;

        vm.SearchText = "voc";

        vm.SelectedCount.Should().Be(0, "a tick on a row the search hides must not survive");

        vm.SelectAllCommand.Execute(null);
        vm.ConfirmDeleteSelectedCommand.Execute(null);

        vm.Snapshot().Select(e => e.Title).Should().BeEquivalentTo("Apple", "Banana");
    }

    [Fact]
    public void ClearSelection_UnticksEverything()
    {
        var vm = WithEntries("A", "B");
        vm.SelectAllCommand.Execute(null);

        vm.ClearSelectionCommand.Execute(null);

        vm.SelectedCount.Should().Be(0);
        vm.Rows.Should().OnlyContain(r => !r.IsSelected);
    }

    [Fact]
    public void RequestingWithNothingTicked_DoesNothing()
    {
        var vm = WithEntries("A");

        vm.RequestDeleteSelectedCommand.Execute(null);

        vm.IsConfirmingBulkDelete.Should().BeFalse();
    }

    [Fact]
    public void UntickingTheLastRow_ClosesTheConfirmation()
    {
        var vm = WithEntries("A");
        vm.Rows[0].IsSelected = true;
        vm.RequestDeleteSelectedCommand.Execute(null);

        vm.Rows[0].IsSelected = false;

        vm.IsConfirmingBulkDelete.Should().BeFalse();
    }

    [Fact]
    public void ReloadingTheVault_ForgetsTheSelection()
    {
        var vm = WithEntries("A", "B");
        vm.SelectAllCommand.Execute(null);

        vm.Load([new PasswordEntry { Title = "X", Password = "p" }]);

        vm.SelectedCount.Should().Be(0);
        vm.Rows.Should().ContainSingle().Which.IsSelected.Should().BeFalse();
    }
}
