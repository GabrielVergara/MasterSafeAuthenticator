// File:    Program.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using MasterSafe.App.Services;

namespace MasterSafe.App;

/// <summary>Application entry point.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First of all: before any library or window loads, and long before a password can be typed.
        ProcessHardening.Apply(Path.GetFileName(Environment.ProcessPath));

        // One copy per user: two running copies could each save, and the second would overwrite the first.
        var folder = AppPaths.Default.DataFolder;
        using var instance = SingleInstance.TryAcquire(folder);
        if (instance is null)
        {
            if (!SingleInstance.SignalRunningCopy(folder))
            {
                SingleInstance.ReportStuckCopy();
            }

            return;
        }

        Instance = instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The lock of this running copy; null when the app was not started through <see cref="Main"/>.</summary>
    internal static SingleInstance? Instance { get; private set; }

    /// <summary>Avalonia configuration used by the runtime and the designer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
