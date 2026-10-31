// File:    AutoRecoveryBackup.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Text.RegularExpressions;

namespace MasterSafe.App.Services;

/// <summary>
/// Runs an action a while after the last time it was asked for. Asking again starts the wait over, so a burst of changes
/// leads to one run, shortly after the last change.
/// </summary>
/// <param name="startTimer">Starts a one-shot timer; disposing the result cancels it.</param>
/// <param name="delay">How long to wait after the last request.</param>
/// <param name="action">What to run.</param>
public sealed class DebouncedAction(Func<TimeSpan, Func<Task>, IDisposable> startTimer, TimeSpan delay, Func<Task> action)
{
    private IDisposable? _timer;
    private int _generation;

    /// <summary>True while a run is waiting.</summary>
    public bool IsPending => _timer is not null;

    /// <summary>Asks for a run; any earlier wait is replaced.</summary>
    public void Trigger()
    {
        Cancel();
        var generation = _generation;
        _timer = startTimer(delay, async () =>
        {
            if (generation != _generation)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            await action();
        });
    }

    /// <summary>Forgets a waiting run.</summary>
    public void Cancel()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
    }
}

/// <summary>Housekeeping for the folder that automatic recovery backups are written to.</summary>
public static partial class AutoRecoveryBackup
{
    /// <summary>How many automatic backups are kept.</summary>
    public const int KeepNewest = 5;

    /// <summary>Quiet time after the last change before a backup is written.</summary>
    public static readonly TimeSpan QuietTime = TimeSpan.FromSeconds(60);

    /// <summary>The least time between two automatic backups.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(5);

    // Exactly the names the app gives its recovery backups, for example recovery-20261101-143015.sauth.
    [GeneratedRegex(@"^recovery-\d{8}-\d{6}\.sauth$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();

    /// <summary>True for a file name the app uses for a recovery backup. Nothing else is ever deleted.</summary>
    public static bool IsBackupName(string fileName) => BackupName().IsMatch(fileName);

    /// <summary>
    /// Deletes the oldest recovery backups in <paramref name="folder"/> so that <paramref name="keep"/> remain. Only files
    /// whose names match the app's own pattern are considered, and the newest ones are never touched.
    /// Returns how many files were deleted.
    /// </summary>
    public static int Prune(string folder, int keep = KeepNewest)
    {
        if (keep < 1 || !Directory.Exists(folder))
        {
            return 0;
        }

        var names = Directory.EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .Where(n => n is not null && IsBackupName(n))
            .Select(n => n!)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .ToList();

        var deleted = 0;
        foreach (var name in names.Skip(keep))
        {
            try
            {
                File.Delete(Path.Combine(folder, name));
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be removed now is tried again next time.
            }
        }

        return deleted;
    }
}
