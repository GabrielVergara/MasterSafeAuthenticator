// File:    IFileDialogs.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace MasterSafe.App.Services;

/// <summary>File and folder pickers, behind an interface so view models can be tested without a window.</summary>
public interface IFileDialogs
{
    /// <summary>Asks the user to choose a file. Returns null when cancelled.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="description">Name of the file type, for example "Vault files".</param>
    /// <param name="patterns">File patterns such as <c>*.sauth</c>.</param>
    Task<string?> OpenFileAsync(string title, string description, params string[] patterns);

    /// <summary>Like <see cref="OpenFileAsync"/>, starting in <paramref name="startFolder"/> when it exists.</summary>
    Task<string?> OpenFileInAsync(string title, string? startFolder, string description, params string[] patterns)
        => OpenFileAsync(title, description, patterns);

    /// <summary>Asks the user where to save a file, starting in <paramref name="startFolder"/> when given. Returns null when cancelled.</summary>
    Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string? startFolder = null);

    /// <summary>Asks the user to choose a folder. Returns null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Shows a file in the system file manager.</summary>
    void ShowInFolder(string path);
}

/// <summary>Pickers backed by the application window's storage provider.</summary>
public sealed class WindowFileDialogs(Func<TopLevel?> owner) : IFileDialogs
{
    /// <inheritdoc />
    public Task<string?> OpenFileAsync(string title, string description, params string[] patterns)
        => OpenFileInAsync(title, null, description, patterns);

    /// <inheritdoc />
    public async Task<string?> OpenFileInAsync(string title, string? startFolder, string description, params string[] patterns)
    {
        var storage = owner()?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var start = !string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder)
            ? await storage.TryGetFolderFromPathAsync(startFolder)
            : null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = start,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = patterns }, FilePickerFileTypes.All],
        });

        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string? startFolder = null)
    {
        var storage = owner()?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var start = !string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder)
            ? await storage.TryGetFolderFromPathAsync(startFolder)
            : null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = start,
        });

        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title)
    {
        var storage = owner()?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public void ShowInFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var start = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", full } }
                : new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { Path.GetDirectoryName(full) ?? full } };
            start.UseShellExecute = false;
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No file manager available; nothing else to do.
        }
    }
}
