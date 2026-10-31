// File:    PasswordsViewModel.Attachments.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;

namespace MasterSafe.App.ViewModels;

/// <summary>One attached file as shown in the entry form.</summary>
public sealed record AttachmentRow(PasswordAttachment Attachment)
{
    /// <summary>File name.</summary>
    public string Name => Attachment.Name;

    /// <summary>Size for people, for example "3.2 MB".</summary>
    public string SizeText => AttachmentLimits.Describe(Attachment.Size);
}

/// <summary>Files attached to password entries. The bytes stay in memory only while the vault is unlocked.</summary>
public sealed partial class PasswordsViewModel
{
    // Bytes of every attachment of an entry, plus files added in the form that are not saved yet.
    private readonly Dictionary<Guid, byte[]> _blobs = [];

    /// <summary>The files attached to the entry in the form.</summary>
    public ObservableCollection<AttachmentRow> FormAttachments { get; } = [];

    /// <summary>File pickers; set by the shell.</summary>
    public IFileDialogs? Dialogs { get; set; }

    /// <summary>Total size of all attached files, in bytes.</summary>
    public long AttachmentBytes => _blobs.Values.Sum(b => (long)b.Length);

    /// <summary>The attachment bytes that saved entries refer to, for saving in the vault.</summary>
    public IReadOnlyDictionary<Guid, byte[]> AttachmentSnapshot()
    {
        var referenced = _all.SelectMany(e => e.Attachments).Select(a => a.Id).ToHashSet();
        return _blobs.Where(b => referenced.Contains(b.Key)).ToDictionary(b => b.Key, b => b.Value);
    }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.OpenFileAsync("Choose a file to attach", "All files", "*");
        if (path is not null)
        {
            AttachFile(path);
        }
    }

    /// <summary>Adds a file to the entry in the form. The file is saved with the entry. Returns false and says why when it cannot be attached.</summary>
    public bool AttachFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                Status = "That file does not exist.";
                return false;
            }

            var name = AttachmentLimits.SafeName(info.Name);
            if (info.Length > AttachmentLimits.MaxFileBytes)
            {
                Status = $"{name} is {AttachmentLimits.Describe(info.Length)}. Files up to {AttachmentLimits.Describe(AttachmentLimits.MaxFileBytes)} can be attached.";
                return false;
            }

            if (AttachmentBytes + info.Length > AttachmentLimits.MaxTotalBytes || _blobs.Count >= AttachmentLimits.MaxCount)
            {
                Status = $"The vault already holds {AttachmentLimits.Describe(AttachmentBytes)} of attachments. The limit is {AttachmentLimits.Describe(AttachmentLimits.MaxTotalBytes)}.";
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var attachment = new PasswordAttachment { Name = name, Size = bytes.Length };
            _blobs[attachment.Id] = bytes;
            FormAttachments.Add(new AttachmentRow(attachment));
            Status = $"Attached {name} ({AttachmentLimits.Describe(bytes.Length)}). Choose Save to keep it with the entry.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"The file could not be read: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentRow? row)
    {
        if (row is null || !FormAttachments.Remove(row))
        {
            return;
        }

        Status = $"Removed {row.Name} from the entry. Choose Save to keep the change.";
    }

    [RelayCommand]
    private async Task SaveAttachmentAsync(AttachmentRow? row)
    {
        if (row is null || Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.SaveFileAsync("Save attachment", row.Name, Path.GetExtension(row.Name).TrimStart('.'));
        if (path is not null)
        {
            SaveAttachmentTo(row, path);
        }
    }

    /// <summary>Writes an attached file out to disk. The copy is no longer protected by the vault.</summary>
    public bool SaveAttachmentTo(AttachmentRow row, string path)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!_blobs.TryGetValue(row.Attachment.Id, out var bytes))
        {
            Status = "The file is not available.";
            return false;
        }

        try
        {
            File.WriteAllBytes(path, bytes);
            Status = $"Saved {row.Name}. The copy is not protected by your vault: delete it when you are done.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"The file could not be saved: {ex.Message}";
            return false;
        }
    }

    /// <summary>Forgets attachment bytes that no entry and no form refers to any more, wiping them from memory.</summary>
    private void PruneBlobs()
    {
        var keep = _all.SelectMany(e => e.Attachments).Select(a => a.Id)
            .Concat(FormAttachments.Select(r => r.Attachment.Id))
            .ToHashSet();
        foreach (var id in _blobs.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            CryptographicOperations.ZeroMemory(_blobs[id]);
            _blobs.Remove(id);
        }
    }

    private void ClearBlobs()
    {
        foreach (var bytes in _blobs.Values)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        _blobs.Clear();
        FormAttachments.Clear();
    }

    /// <summary>
    /// Keeps only the attachments of imported entries that fit the limits and whose bytes were supplied.
    /// Returns the entries to add and how many attachments were left out.
    /// </summary>
    private (List<PasswordEntry> Entries, int Dropped) FitAttachments(IEnumerable<PasswordEntry> entries, IReadOnlyDictionary<Guid, byte[]>? supplied)
    {
        var result = new List<PasswordEntry>();
        var dropped = 0;
        var total = AttachmentBytes;
        foreach (var entry in entries)
        {
            if (entry.Attachments.Count == 0)
            {
                result.Add(entry);
                continue;
            }

            var kept = new List<PasswordAttachment>();
            foreach (var attachment in entry.Attachments)
            {
                if (supplied is not null
                    && supplied.TryGetValue(attachment.Id, out var bytes)
                    && bytes.Length <= AttachmentLimits.MaxFileBytes
                    && total + bytes.Length <= AttachmentLimits.MaxTotalBytes
                    && _blobs.Count < AttachmentLimits.MaxCount)
                {
                    _blobs[attachment.Id] = bytes;
                    total += bytes.Length;
                    kept.Add(new PasswordAttachment { Id = attachment.Id, Name = AttachmentLimits.SafeName(attachment.Name), Size = bytes.Length });
                }
                else
                {
                    dropped++;
                }
            }

            result.Add(entry.WithAttachments(kept));
        }

        return (result, dropped);
    }
}
