// File:    HeadlessUi.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;


namespace MasterSafe.Tests;

/// <summary>
/// Runs the real application without a screen on one dedicated thread, so tests can open the real windows and pages.
/// All work that touches the UI goes through <see cref="Run"/>.
/// </summary>
public static class HeadlessUi
{
    private static readonly Lazy<object> Started = new(Start);

    private static object Start()
    {
        var ready = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<MasterSafe.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true }).SetupWithoutStarting();
                ready.Set();
                Dispatcher.UIThread.MainLoop(CancellationToken.None);
            }
            catch (Exception ex)
            {
                failure = ex;
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Headless UI",
        };
        thread.Start();
        ready.Wait();
        return failure is null ? new object() : throw new InvalidOperationException("The headless UI could not start.", failure);
    }

    // Avalonia gives its UI dispatcher to the first thread that asks for it. Starting the headless UI as soon as the test
    // assembly is loaded makes sure that thread is ours, whatever test runs first, and lets any test that posts to the
    // UI thread be served by its loop instead of waiting forever.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void EnsureStarted() => _ = Started.Value;

    /// <summary>Runs <paramref name="work"/> on the UI thread and waits for it. Exceptions and failed assertions come back to the caller.</summary>
    public static void Run(Action work)
    {
        _ = Started.Value;
        Dispatcher.UIThread.InvokeAsync(work).GetAwaiter().GetResult();
    }
}
