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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia configuration used by the runtime and the designer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
