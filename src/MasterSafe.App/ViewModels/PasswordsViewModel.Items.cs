// File:    PasswordsViewModel.Items.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;
using MasterSafe.Crypto;

namespace MasterSafe.App.ViewModels;

/// <summary>One kind of item that can be chosen in the entry form.</summary>
public sealed record EntryKindOption(EntryKind Kind, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>One extra field in the entry form.</summary>
public sealed partial class FieldRow : ObservableObject
{
    /// <summary>Name of the field.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Its value.</summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>True when the value is hidden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlain))]
    private bool _isSecret;

    /// <summary>True when the value is shown.</summary>
    public bool IsPlain => !IsSecret;
}

/// <summary>Item kinds, extra fields, expiry dates, tags, website icons and permanent deletions.</summary>
public sealed partial class PasswordsViewModel
{
    private readonly Dictionary<Guid, DateTimeOffset> _tombstones = [];
    private string? _formIcon;

    /// <summary>The kinds of item that can be created.</summary>
    public static IReadOnlyList<EntryKindOption> KindOptions { get; } =
    [
        new(EntryKind.Login, "Login"),
        new(EntryKind.Note, "Secure note"),
        new(EntryKind.Card, "Payment card"),
        new(EntryKind.Identity, "Identity"),
    ];

    /// <summary>The kind of item in the form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoginForm))]
    private EntryKindOption _formKind = KindOptions[0];

    /// <summary>True when the form shows the fields of a sign-in.</summary>
    public bool IsLoginForm => FormKind.Kind == EntryKind.Login;

    /// <summary>The extra fields in the form.</summary>
    public ObservableCollection<FieldRow> FormFields { get; } = [];

    /// <summary>The date the password should be changed by.</summary>
    [ObservableProperty]
    private DateTime? _formExpires;

    /// <summary>The tags in use, for the filter. The first one means all.</summary>
    public ObservableCollection<string> TagChoices { get; } = [];

    /// <summary>The tag the list is filtered by. The first choice shows everything.</summary>
    [ObservableProperty]
    private string? _selectedTag;

    /// <summary>True when tags are in use, so the filter is worth showing.</summary>
    public bool HasTags => TagChoices.Count > 1;

    partial void OnFormKindChanged(EntryKindOption value)
    {
        // A new item starts with the fields its kind needs. An item being edited keeps what it has.
        if (_loadingForm || _editingId is not null || FormFields.Count > 0 || value is null)
        {
            return;
        }

        foreach (var (name, secret) in TemplateFor(value.Kind))
        {
            FormFields.Add(new FieldRow { Name = Loc.T(name), IsSecret = secret });
        }
    }

    private static (string Name, bool Secret)[] TemplateFor(EntryKind kind) => kind switch
    {
        EntryKind.Card => [("Cardholder name", false), ("Card number", true), ("Expiry", false), ("Security code", true), ("PIN", true)],
        EntryKind.Identity => [("Full name", false), ("Email", false), ("Phone", false), ("Address", false), ("Passport or ID number", true)],
        _ => [],
    };

    private bool _loadingForm;

    /// <summary>Adds an empty field to the form.</summary>
    [RelayCommand]
    private void AddField()
    {
        if (FormFields.Count >= CustomField.MaxPerEntry)
        {
            Status = $"An item can have up to {CustomField.MaxPerEntry} fields.";
            return;
        }

        FormFields.Add(new FieldRow());
    }

    /// <summary>Adds a hidden field to the form.</summary>
    [RelayCommand]
    private void AddSecretField()
    {
        AddField();
        if (FormFields.Count > 0)
        {
            FormFields[^1].IsSecret = true;
        }
    }

    /// <summary>Removes a field from the form.</summary>
    [RelayCommand]
    private void RemoveField(FieldRow? row)
    {
        if (row is not null)
        {
            FormFields.Remove(row);
        }
    }

    /// <summary>Copies the value of a field. A hidden one is treated like a password and cleared from the clipboard.</summary>
    [RelayCommand]
    private void CopyField(FieldRow? row)
    {
        if (row is null || row.Value.Length == 0)
        {
            return;
        }

        CopyRequested?.Invoke(this, row.Value);
        Status = $"Copied {row.Name}. The clipboard clears in {ClipboardPolicy.ClearAfterText}.";
    }

    /// <summary>Removes the expiry date from the form.</summary>
    [RelayCommand]
    private void ClearExpiry() => FormExpires = null;

    private IReadOnlyList<CustomField> FieldsFromForm()
        => FormFields
            .Where(f => f.Name.Trim().Length > 0 || f.Value.Length > 0)
            .Take(CustomField.MaxPerEntry)
            .Select(f => new CustomField { Name = f.Name.Trim(), Value = f.Value, IsSecret = f.IsSecret })
            .ToList();

    private void ShowExtras(PasswordEntry entry)
    {
        _loadingForm = true;
        FormKind = KindOptions.FirstOrDefault(o => o.Kind == entry.Kind) ?? KindOptions[0];
        _loadingForm = false;

        FormFields.Clear();
        foreach (var field in entry.Fields)
        {
            FormFields.Add(new FieldRow { Name = field.Name, Value = field.Value, IsSecret = field.IsSecret });
        }

        FormExpires = entry.ExpiresUtc?.LocalDateTime.Date;
        _formIcon = entry.IconPng;
        ClearIconPreview();
        OnPropertyChanged(nameof(HasFormIcon));
    }

    private void ClearExtras()
    {
        _loadingForm = true;
        FormKind = KindOptions[0];
        _loadingForm = false;
        FormFields.Clear();
        FormExpires = null;
        _formIcon = null;
        ClearIconPreview();
        OnPropertyChanged(nameof(HasFormIcon));
    }

    // ------------------------------------------------------------------ tags

    private void RebuildTags()
    {
        var wanted = SelectedTag;
        var tags = _all.Where(e => !e.IsDeleted).SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        TagChoices.Clear();
        TagChoices.Add(Loc.T("All tags"));
        foreach (var tag in tags)
        {
            TagChoices.Add(tag);
        }

        _keepingTag = true;
        SelectedTag = wanted is not null && TagChoices.Contains(wanted) ? wanted : TagChoices[0];
        _keepingTag = false;
        OnPropertyChanged(nameof(HasTags));
    }

    private bool _keepingTag;

    partial void OnSelectedTagChanged(string? value)
    {
        if (_keepingTag)
        {
            return;
        }

        // A tick on an entry the filter hides could delete something the user can no longer see.
        _selected.Clear();
        IsConfirmingBulkDelete = false;
        FilterEntries();
    }

    private bool MatchesTag(PasswordEntry entry)
        => SelectedTag is null || TagChoices.Count == 0 || SelectedTag == TagChoices[0] || entry.Tags.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ website icon

    /// <summary>True when the user allowed website icons in Settings. Set by the shell.</summary>
    public Func<bool> IconsAllowed { get; set; } = () => false;

    /// <summary>Gets a site's icon. Replaced in tests.</summary>
    public Func<string?, Task<IconResult>> IconFetcher { get; set; } = url => SharedFetcher.Value.FetchAsync(url);

    private static readonly Lazy<WebsiteIconFetcher> SharedFetcher = new(() => new WebsiteIconFetcher());

    /// <summary>The icon waiting for the user to accept it (a 32 by 32 PNG, Base64).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIconPreview))]
    private string? _iconPreview;

    /// <summary>True while an icon waits to be accepted.</summary>
    public bool HasIconPreview => IconPreview is not null;

    /// <summary>True when the entry in the form has an icon.</summary>
    public bool HasFormIcon => _formIcon is not null;

    /// <summary>True when the "use the site's icon" button is offered.</summary>
    public bool ShowIconButton => IconsAllowed() && IsLoginForm;

    /// <summary>Tells the view that the setting may have changed.</summary>
    public void RefreshIconSetting() => OnPropertyChanged(nameof(ShowIconButton));

    /// <summary>True while an icon is being fetched.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchIconCommand))]
    private bool _isFetchingIcon;

    private bool CanFetchIcon => !IsFetchingIcon;

    private void ClearIconPreview() => IconPreview = null;

    /// <summary>Asks the site in the form for its icon. Only for this one entry, only on request, and only when allowed in Settings.</summary>
    [RelayCommand(CanExecute = nameof(CanFetchIcon))]
    private async Task FetchIconAsync()
    {
        if (!IconsAllowed())
        {
            Status = "Website icons are off. You can turn them on in Settings.";
            return;
        }

        IsFetchingIcon = true;
        try
        {
            var result = await IconFetcher(Url);
            IconPreview = result.PngBase64;
            Status = result.Problem ?? "Is this the right icon? An icon does not prove that a site is genuine: a fake site can copy one.";
        }
        finally
        {
            IsFetchingIcon = false;
        }
    }

    /// <summary>Keeps the icon in the preview. It is saved with the entry.</summary>
    [RelayCommand]
    private void AcceptIcon()
    {
        _formIcon = IconPreview;
        IconPreview = null;
        OnPropertyChanged(nameof(HasFormIcon));
        Status = "The icon is kept when you choose Save.";
    }

    /// <summary>Throws the preview away.</summary>
    [RelayCommand]
    private void RejectIcon() => IconPreview = null;

    /// <summary>Goes back to the letter icon.</summary>
    [RelayCommand]
    private void RemoveIcon()
    {
        _formIcon = null;
        OnPropertyChanged(nameof(HasFormIcon));
    }

    // ------------------------------------------------------------------ fixes offered by the health page

    /// <summary>Moves an entry to the recycle bin. Used for the copies the health page finds.</summary>
    public void MoveToBinById(Guid id) => Delete(_all.Find(e => e.Id == id && !e.IsDeleted));

    /// <summary>Changes the address of an entry to https. The old value goes into its history.</summary>
    public bool UseHttpsById(Guid id)
    {
        var index = _all.FindIndex(e => e.Id == id && !e.IsDeleted);
        var https = index < 0 ? null : PasswordHealth.HttpsVersion(_all[index].Url);
        if (https is null)
        {
            return false;
        }

        var entry = _all[index];
        _all[index] = entry.WithHistory(BuildHistory(entry, entry.WithUrl(https))).WithUrl(https);
        Status = $"{entry.Title} now uses https. If the site does not open, the old address is in its history.";
        ApplyFilter();
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // ------------------------------------------------------------------ permanent deletions

    /// <summary>Items deleted for good, for sync.</summary>
    public IReadOnlyList<Tombstone> TombstoneSnapshot()
        => _tombstones.Select(t => new Tombstone { Id = t.Key, DeletedUtc = t.Value }).ToList();

    /// <summary>True when the form holds something that a reload of the list would throw away.</summary>
    public bool IsFormInUse
        => _editingId is not null || Title.Length > 0 || UserName.Length > 0 || Password.Length > 0 || Url.Length > 0 || Notes.Length > 0 || FormFields.Count > 0;

    private void Forget(IEnumerable<PasswordEntry> entries)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            _tombstones[entry.Id] = now;
        }
    }

    private void LoadTombstones(IEnumerable<Tombstone>? tombstones)
    {
        _tombstones.Clear();
        foreach (var tombstone in tombstones ?? [])
        {
            _tombstones[tombstone.Id] = tombstone.DeletedUtc;
        }
    }
}
