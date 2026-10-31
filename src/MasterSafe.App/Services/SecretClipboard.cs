// File:    SecretClipboard.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia.Input;

namespace MasterSafe.App.Services;

/// <summary>
/// Builds what is put on the clipboard for a secret. Besides the text it carries the markers that tell the system and
/// clipboard managers not to remember it: without them Windows keeps a copy in its clipboard history (Win+V) and may
/// send it to the cloud clipboard, so the copy would survive the app clearing the clipboard.
/// </summary>
public static class SecretClipboard
{
    /// <summary>Windows: do not put this in the clipboard history. The value is a DWORD, 0 means no.</summary>
    public const string WindowsHistoryFormat = "CanIncludeInClipboardHistory";

    /// <summary>Windows: do not sync this to other devices. The value is a DWORD, 0 means no.</summary>
    public const string WindowsCloudFormat = "CanUploadToCloudClipboard";

    /// <summary>Windows: clipboard monitors should not look at this. Present means yes.</summary>
    public const string WindowsMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>KDE and other Linux clipboard managers: this comes from a password manager. The value is "secret".</summary>
    public const string LinuxPasswordHintFormat = "x-kde-passwordManagerHint";

    private static readonly byte[] DwordZero = [0, 0, 0, 0];
    private static readonly byte[] DwordOne = [1, 0, 0, 0];

    /// <summary>The names of the markers that are added on this system, in addition to the text.</summary>
    public static IReadOnlyList<string> MarkerNames => OperatingSystem.IsWindows()
        ? [WindowsHistoryFormat, WindowsCloudFormat, WindowsMonitorFormat]
        : OperatingSystem.IsLinux()
            ? [LinuxPasswordHintFormat]
            : [];

    /// <summary>Creates the clipboard content for a secret.</summary>
    public static DataTransfer Create(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var item = DataTransferItem.CreateText(secret);
        if (OperatingSystem.IsWindows())
        {
            item.Set(DataFormat.CreateBytesPlatformFormat(WindowsHistoryFormat), DwordZero);
            item.Set(DataFormat.CreateBytesPlatformFormat(WindowsCloudFormat), DwordZero);
            item.Set(DataFormat.CreateBytesPlatformFormat(WindowsMonitorFormat), DwordOne);
        }
        else if (OperatingSystem.IsLinux())
        {
            item.Set(DataFormat.CreateStringPlatformFormat(LinuxPasswordHintFormat), "secret");
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }
}
