// File:    ImportViewModel.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterSafe.App.Services;
using MasterSafe.Core;
using MasterSafe.QR;

namespace MasterSafe.App.ViewModels;

/// <summary>
/// Imports authenticator accounts from an <c>otpauth://</c> link, or from a QR code image that was dropped on the
/// window or chosen with a file picker.
/// </summary>
public sealed partial class ImportViewModel : ViewModelBase
{
    /// <summary>Most images imported in one drop.</summary>
    public const int MaxImagesPerDrop = 20;

    private readonly QrCodeImporter _importer = new();

    [ObservableProperty]
    private string _manualUri = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private OtpAccount? _importedAccount;

    /// <summary>Raised for each account that was imported and should be added to the vault.</summary>
    public event EventHandler<OtpAccount>? AccountImported;

    /// <summary>File pickers; set by the shell. Without it the "Choose an image" button does nothing.</summary>
    public IFileDialogs? Dialogs { get; set; }

    /// <summary>Returns true when the account is already in the vault; set by the shell.</summary>
    public Func<OtpAccount, bool>? IsDuplicate { get; set; }

    [RelayCommand]
    private void ImportUri()
    {
        if (ImportFromUri())
        {
            ManualUri = string.Empty;
        }
    }

    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.OpenFileAsync("Choose a QR code image", "Images", "*.png", "*.jpg", "*.jpeg", "*.bmp");
        if (path is not null)
        {
            ImportFromImage(path);
        }
    }

    /// <summary>Imports an account from the <c>otpauth://</c> link typed or pasted by the user.</summary>
    public bool ImportFromUri()
    {
        if (OtpAuthParser.TryParse(ManualUri.Trim(), out var account) && account is not null)
        {
            return Accept(account, "from the link");
        }

        Status = "Not a valid otpauth:// link.";
        return false;
    }

    /// <summary>Imports an account from a QR code image file.</summary>
    public bool ImportFromImage(string path)
    {
        if (!QrImageFormats.IsSupported(path))
        {
            Status = $"{Path.GetFileName(path)} is not a PNG, JPG or BMP image.";
            return false;
        }

        try
        {
            if (_importer.TryImportFromFile(path, out var account) && account is not null)
            {
                return Accept(account, "from the image");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or QrImportException or NotSupportedException)
        {
            Status = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }

        Status = $"No valid authenticator QR code was found in {Path.GetFileName(path)}.";
        return false;
    }

    /// <summary>
    /// Imports every QR code image in <paramref name="paths"/> (up to <see cref="MaxImagesPerDrop"/>). The status
    /// summarises the result. Returns the number of accounts added.
    /// </summary>
    public int ImportFromImages(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var files = paths.Take(MaxImagesPerDrop + 1).ToList();
        if (files.Count == 0)
        {
            Status = "No file was dropped.";
            return 0;
        }

        var tooMany = files.Count > MaxImagesPerDrop;
        files = files.Take(MaxImagesPerDrop).ToList();

        if (files.Count == 1)
        {
            return ImportFromImage(files[0]) ? 1 : 0;
        }

        var imported = 0;
        foreach (var file in files)
        {
            if (ImportFromImage(file))
            {
                imported++;
            }
        }

        Status = $"Imported {imported} of {files.Count} images."
            + (tooMany ? $" Only the first {MaxImagesPerDrop} were read." : string.Empty);
        return imported;
    }

    /// <summary>Imports an account from a QR code image stream.</summary>
    public bool ImportFromStream(Stream stream)
    {
        try
        {
            return Accept(_importer.ImportFromStream(stream), "from the image");
        }
        catch (QrImportException ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    private bool Accept(OtpAccount account, string source)
    {
        var name = string.IsNullOrWhiteSpace(account.Issuer) ? account.Label : $"{account.Issuer} ({account.Label})";

        if (IsDuplicate?.Invoke(account) == true)
        {
            Status = $"{name} is already in your vault.";
            return false;
        }

        // The status is set first so whoever handles the event can show it.
        ImportedAccount = account;
        Status = $"Imported {name} {source}.";
        AccountImported?.Invoke(this, account);
        return true;
    }
}
