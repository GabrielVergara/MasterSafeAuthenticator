// File:    ImportViewModel.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeAuthenticator.Core;
using SafeAuthenticator.QR;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// Import screen: accepts otpauth URIs or QR images (png/jpg/jpeg/bmp/clipboard/drag-drop).
/// </summary>
public sealed partial class ImportViewModel : ViewModelBase
{
    private readonly QrCodeImporter _importer = new();

    [ObservableProperty]
    private string _manualUri = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private OtpAccount? _importedAccount;

    /// <summary>Raised when an account was successfully imported.</summary>
    public event EventHandler<OtpAccount>? AccountImported;

    [RelayCommand]
    private void ImportUri()
    {
        if (ImportFromUri())
        {
            ManualUri = string.Empty;
        }
    }

    /// <summary>Imports from a manually pasted otpauth URI.</summary>
    public bool ImportFromUri()
    {
        if (OtpAuthParser.TryParse(ManualUri.Trim(), out var account) && account is not null)
        {
            ImportedAccount = account;
            AccountImported?.Invoke(this, account);
            Status = $"Imported {account.Issuer} ({account.Label}).";
            return true;
        }

        Status = "Not a valid otpauth:// URI.";
        return false;
    }

    /// <summary>Imports from a QR image file path.</summary>
    public bool ImportFromImage(string path)
    {
        if (_importer.TryImportFromFile(path, out var account) && account is not null)
        {
            ImportedAccount = account;
            AccountImported?.Invoke(this, account);
            Status = $"Imported {account.Issuer} from image.";
            return true;
        }

        Status = "No valid otpauth QR code found in the image.";
        return false;
    }

    /// <summary>Imports from a QR image stream (clipboard / drag-drop).</summary>
    public bool ImportFromStream(Stream stream)
    {
        try
        {
            ImportedAccount = _importer.ImportFromStream(stream);
            AccountImported?.Invoke(this, ImportedAccount);
            Status = $"Imported {ImportedAccount.Issuer}.";
            return true;
        }
        catch (QrImportException ex)
        {
            Status = ex.Message;
            return false;
        }
    }
}
