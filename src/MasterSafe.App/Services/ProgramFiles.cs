// File:    ProgramFiles.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.PostQuantum;

namespace MasterSafe.App.Services;

/// <summary>
/// The check of MasterSafe's own program files: at start-up against the list in the program folder and, when the user
/// allows MasterSafe to go online, that list against the signed copy of the release. It never blocks anything; a problem
/// is shown so the user can reinstall from the official download.
/// </summary>
public static class ProgramFiles
{
    private static readonly Lock Gate = new();
    private static InstalledFilesReport? _report;
    private static bool? _signed;

    /// <summary>Raised when a result arrives, on whatever thread found it.</summary>
    public static event EventHandler? Changed;

    /// <summary>What the check of the program folder found; null until it ran.</summary>
    public static InstalledFilesReport? Report
    {
        get
        {
            lock (Gate)
            {
                return _report;
            }
        }
    }

    /// <summary>True when the list matches the signed release, false when it does not, null when that was not checked.</summary>
    public static bool? Signed
    {
        get
        {
            lock (Gate)
            {
                return _signed;
            }
        }
    }

    /// <summary>True when something is wrong and the user should reinstall.</summary>
    public static bool HasProblem => Report is { Available: true, Ok: false } || Signed == false;

    /// <summary>Checks the program folder. Takes a second or so, so call it off the UI thread.</summary>
    public static void CheckFolder(string folder)
    {
        InstalledFilesReport report;
        try
        {
            report = InstalledFiles.Check(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report = new InstalledFilesReport(true, null, null, [], ["(the program folder could not be read)"], []);
        }

        lock (Gate)
        {
            _report = report;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Compares the list in the program folder with the signed release, once the folder check found it.</summary>
    public static async Task ConfirmAsync(UpdateChecker checker, string folder)
    {
        var path = Path.Combine(folder, InstalledFiles.FileName);
        if (Report is not { Available: true } || !File.Exists(path) || new FileInfo(path).Length > InstalledFiles.MaxBytes)
        {
            return;
        }

        bool? signed;
        try
        {
            signed = await checker.ConfirmInstalledFilesAsync(await File.ReadAllBytesAsync(path)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            return;
        }

        lock (Gate)
        {
            _signed = signed;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>One line for the About window.</summary>
    public static string Describe()
    {
        var report = Report;
        if (report is null)
        {
            return "Program files: not checked yet.";
        }

        if (!report.Available)
        {
            return "Program files: this copy has no list of its files (for example a build from source), so they are not checked.";
        }

        if (!report.Ok)
        {
            return $"Program files: some were changed, are missing or were added ({Problems(report)}). Reinstall MasterSafe from the official download.";
        }

        return Signed switch
        {
            true => $"Program files: unchanged, and their list matches the signed release {report.Version}.",
            false => "Program files: their list does not match the signed release. Reinstall MasterSafe from the official download.",
            null => "Program files: unchanged. Their list is compared with the signed release when MasterSafe may look for updates.",
        };
    }

    /// <summary>The files concerned, at most five, for the message.</summary>
    public static string Problems(InstalledFilesReport report)
    {
        var files = report.Changed.Concat(report.Missing).Concat(report.Unexpected).ToList();
        return string.Join(", ", files.Take(5)) + (files.Count > 5 ? ", ..." : string.Empty);
    }

    /// <summary>For tests: forget earlier results.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _report = null;
            _signed = null;
        }
    }
}
