// File:    HealthViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One line of the health report.</summary>
public sealed record HealthRow(PasswordEntry Entry, string Reason)
{
    /// <summary>Title and folder, for example "GitHub (Work)".</summary>
    public string Title => Entry.Folder.Length > 0 ? $"{Entry.Title} ({Entry.Folder})" : Entry.Title;
}

/// <summary>
/// The Password health page: lists passwords that are weak, reused or old. An optional, separate check looks passwords up
/// in known data breaches; it only runs when the user has allowed it in Settings.
/// </summary>
public sealed partial class HealthViewModel : ViewModelBase
{
    /// <summary>Most different passwords looked up in one breach check.</summary>
    public const int MaxBreachLookups = 300;

    private readonly Func<IReadOnlyList<PasswordEntry>> _entries;
    private readonly Func<bool> _breachCheckAllowed;
    private readonly BreachChecker? _checker;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<Guid, int> _breachCounts = [];

    /// <summary>Creates the page.</summary>
    /// <param name="entries">The password entries to look at.</param>
    /// <param name="breachCheckAllowed">True when the user allowed the optional breach check.</param>
    /// <param name="checker">The breach service client; without it the breach check is unavailable.</param>
    /// <param name="clock">The current time.</param>
    public HealthViewModel(
        Func<IReadOnlyList<PasswordEntry>>? entries = null,
        Func<bool>? breachCheckAllowed = null,
        BreachChecker? checker = null,
        Func<DateTimeOffset>? clock = null)
    {
        _entries = entries ?? (() => []);
        _breachCheckAllowed = breachCheckAllowed ?? (() => false);
        _checker = checker;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Passwords that are easy to guess.</summary>
    public ObservableCollection<HealthRow> Weak { get; } = [];

    /// <summary>Passwords used by more than one entry.</summary>
    public ObservableCollection<HealthRow> Reused { get; } = [];

    /// <summary>Passwords not changed for a long time.</summary>
    public ObservableCollection<HealthRow> Old { get; } = [];

    /// <summary>Passwords found in known breaches.</summary>
    public ObservableCollection<HealthRow> Breached { get; } = [];

    /// <summary>One-line summary of the report.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Progress and results of the breach check.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>True while the breach check is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckBreachesCommand))]
    private bool _isChecking;

    /// <summary>True when the breach check was run since the page was opened.</summary>
    [ObservableProperty]
    private bool _breachCheckDone;

    /// <summary>Asks the shell to open an entry for editing.</summary>
    public Action<Guid>? OpenEntry { get; set; }

    /// <summary>True when the user allowed the breach check in Settings.</summary>
    public bool IsBreachCheckAllowed => _breachCheckAllowed();

    /// <summary>Shown under the breach check button.</summary>
    public string BreachCheckNote => IsBreachCheckAllowed
        ? "Only the first 5 characters of each password's SHA-1 hash are sent to api.pwnedpasswords.com. The passwords themselves never leave this computer."
        : "This check is off. To use it, turn on \"Allow checking passwords against known data breaches\" in Settings. It contacts api.pwnedpasswords.com and sends only a short hash prefix of each password.";

    /// <summary>Looks at the passwords again.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsBreachCheckAllowed));
        OnPropertyChanged(nameof(BreachCheckNote));
        CheckBreachesCommand.NotifyCanExecuteChanged();

        var report = PasswordHealth.Analyze(_entries(), _clock(), _breachCounts.Count > 0 ? _breachCounts : null);
        Fill(Weak, report.Weak);
        Fill(Reused, report.Reused);
        Fill(Old, report.Old);
        Fill(Breached, report.Breached);

        Summary = report.Checked == 0
            ? "There are no passwords to check yet."
            : report.IsClean
                ? $"All {report.Checked} passwords look fine."
                : $"{report.EntriesNeedingAttention} of {report.Checked} passwords need attention.";
    }

    private static void Fill(ObservableCollection<HealthRow> target, IReadOnlyList<HealthIssue> issues)
    {
        target.Clear();
        foreach (var issue in issues)
        {
            target.Add(new HealthRow(issue.Entry, issue.Reason));
        }
    }

    [RelayCommand]
    private void Open(HealthRow? row)
    {
        if (row is not null)
        {
            OpenEntry?.Invoke(row.Entry.Id);
        }
    }

    private bool CanCheck => !IsChecking && _checker is not null && IsBreachCheckAllowed;

    /// <summary>Looks the passwords up in known data breaches. Only works when it was allowed in Settings.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckBreachesAsync(CancellationToken cancellationToken)
    {
        if (_checker is null || !IsBreachCheckAllowed)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var entries = _entries().Where(e => !e.IsDeleted && e.Password.Length > 0).ToList();
            var groups = entries.GroupBy(e => e.Password, StringComparer.Ordinal).Take(MaxBreachLookups).ToList();
            _breachCounts.Clear();

            var done = 0;
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Status = $"Checking {++done} of {groups.Count}...";
                var count = await _checker.CountAsync(group.Key, cancellationToken);
                if (count > 0)
                {
                    foreach (var entry in group)
                    {
                        _breachCounts[entry.Id] = count;
                    }
                }
            }

            BreachCheckDone = true;
            Refresh();
            Status = _breachCounts.Count == 0
                ? $"None of the {groups.Count} passwords checked were found in known breaches."
                : $"{_breachCounts.Count} {(_breachCounts.Count == 1 ? "entry uses a password" : "entries use passwords")} found in known breaches. Change them.";
            if (entries.GroupBy(e => e.Password, StringComparer.Ordinal).Count() > MaxBreachLookups)
            {
                Status += $" Only the first {MaxBreachLookups} different passwords were checked.";
            }
        }
        catch (OperationCanceledException)
        {
            Status = "The check was cancelled.";
        }
        catch (HttpRequestException ex)
        {
            Status = $"Could not reach the breach service: {ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Forgets everything on screen (used when the vault is locked).</summary>
    public void ClearSecrets()
    {
        _breachCounts.Clear();
        Weak.Clear();
        Reused.Clear();
        Old.Clear();
        Breached.Clear();
        Summary = string.Empty;
        Status = null;
        BreachCheckDone = false;
    }
}
