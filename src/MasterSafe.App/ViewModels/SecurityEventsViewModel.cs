// File:    SecurityEventsViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Globalization;
using MasterSafe.App.Services;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// The recent security events of this vault, shown in Settings. An unexpected unlock, a run of wrong passwords or an export
/// the user did not make is a sign that someone else had the vault open.
/// </summary>
public sealed class SecurityEventsViewModel : ViewModelBase
{
    public const string Unlocked = "unlocked";
    public const string WrongPasswords = "wrong-passwords";
    public const string PasswordChanged = "password-changed";
    public const string MethodAdded = "method-added";
    public const string MethodRemoved = "method-removed";
    public const string Exported = "exported";
    public const string FileSaved = "file-saved";
    public const string CopyingPaused = "copying-paused";

    private readonly List<SecurityEvent> _events = [];

    /// <summary>The events as lines for the screen, newest first.</summary>
    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>Raised after an event is recorded, so the shell saves the vault.</summary>
    public event EventHandler? Changed;

    /// <summary>The events to save in the vault.</summary>
    public IReadOnlyList<SecurityEvent> Snapshot() => _events.ToList();

    /// <summary>Shows the events of the vault just opened.</summary>
    public void Load(IEnumerable<SecurityEvent> events)
    {
        _events.Clear();
        _events.AddRange(events.OrderByDescending(e => e.Utc).Take(SecurityEvent.MaxKept));
        Refresh();
    }

    /// <summary>Records an event. The detail must never be a secret. Without <paramref name="save"/> the caller saves.</summary>
    public void Record(string kind, string detail = "", bool save = true)
    {
        _events.Insert(0, new SecurityEvent { Utc = DateTimeOffset.UtcNow, Kind = kind, Detail = detail });
        if (_events.Count > SecurityEvent.MaxKept)
        {
            _events.RemoveRange(SecurityEvent.MaxKept, _events.Count - SecurityEvent.MaxKept);
        }

        Refresh();
        if (save)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Forgets the events when the vault locks.</summary>
    public void Clear()
    {
        _events.Clear();
        Lines.Clear();
    }

    /// <summary>One event in words, in the current language.</summary>
    public static string Describe(SecurityEvent e)
    {
        var what = e.Kind switch
        {
            Unlocked => string.Format(CultureInfo.CurrentCulture, Loc.T("Unlocked with {0}"), Loc.T(e.Detail)),
            WrongPasswords => string.Format(CultureInfo.CurrentCulture, Loc.T("{0} wrong master passwords before this unlock"), e.Detail),
            PasswordChanged => Loc.T("Master password changed"),
            MethodAdded => string.Format(CultureInfo.CurrentCulture, Loc.T("Unlock method added: {0}"), e.Detail),
            MethodRemoved => string.Format(CultureInfo.CurrentCulture, Loc.T("Unlock method removed: {0}"), e.Detail),
            Exported => string.Format(CultureInfo.CurrentCulture, Loc.T("Exported: {0}"), Loc.T(e.Detail)),
            FileSaved => Loc.T("An attached file was saved to disk"),
            CopyingPaused => Loc.T("Copying paused: many secrets were copied in a short time"),
            _ => e.Kind,
        };

        return $"{e.Utc.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}  {what}";
    }

    private void Refresh()
    {
        Lines.Clear();
        foreach (var e in _events.Take(50))
        {
            Lines.Add(Describe(e));
        }
    }
}
