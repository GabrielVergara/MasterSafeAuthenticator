// File:    SingleInstance.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MasterSafe.App.Services;

/// <summary>
/// Makes sure one user runs one copy of the program. Two copies could each save the vault, and the second would overwrite the
/// first. The first copy holds a lock file open without sharing; the operating system releases it even after a crash, so
/// a lock cannot be left behind. A second start asks the first copy to come forward and ends.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(FileStream lockFile, string pipeName)
    {
        _lock = lockFile;
        _pipeName = pipeName;
    }

    /// <summary>The name of the lock file in the data folder.</summary>
    public const string LockFileName = "instance.lock";

    /// <summary>Takes the lock. Null when another copy holds it.</summary>
    public static SingleInstance? TryAcquire(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(dataFolder);
            }
            else
            {
                Directory.CreateDirectory(dataFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var stream = new FileStream(Path.Combine(dataFolder, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(stream, PipeNameFor(dataFolder));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Starts listening for later starts. <paramref name="showWindow"/> is called for each one.</summary>
    public void Listen(Action showWindow)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        _ = Task.Run(() => ListenAsync(showWindow, _stop.Token));
    }

    private async Task ListenAsync(Action showWindow, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // CurrentUserOnly: another user on this computer can neither connect nor pretend to be this program.
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                // Nothing is read from the pipe: being connected to is the whole message, so there is nothing to attack.
                var id = BitConverter.GetBytes(Environment.ProcessId);
                await server.WriteAsync(id, cancellationToken).ConfigureAwait(false);
                await server.FlushAsync(cancellationToken).ConfigureAwait(false);
                _ = await server.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false);
                showWindow();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // A client that goes away must not stop the program; the next one is served normally.
                await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Asks the copy that holds the lock to come forward. False when it does not answer.</summary>
    public static bool SignalRunningCopy(string dataFolder, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);

        try
        {
            using var client = new NamedPipeClientStream(".", PipeNameFor(dataFolder), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect((int)(timeout ?? TimeSpan.FromSeconds(3)).TotalMilliseconds);

            var id = new byte[sizeof(int)];
            client.ReadExactly(id);
            AllowToComeForward(BitConverter.ToInt32(id));
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Tells the user why nothing opened. Used when the lock is held but the running copy does not answer.</summary>
    public static void ReportStuckCopy()
    {
        const string message = "MasterSafe is already running, but it did not answer. Close it in the task manager, then start MasterSafe again.";
#if WINDOWS
        _ = MessageBoxW(IntPtr.Zero, message, "MasterSafe", 0x40);
#else
        Console.Error.WriteLine(message);
#endif
    }

    // Windows only lets the program in front hand the front to another one, so the new start gives its turn to the first copy.
    private static void AllowToComeForward(int processId)
    {
#if WINDOWS
        _ = AllowSetForegroundWindow(processId);
#else
        _ = processId;
#endif
    }

#if WINDOWS
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
#endif

    private static string PipeNameFor(string dataFolder)
    {
        // Short, and the same for every start with this data folder. Unix sockets allow only short names.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataFolder).ToUpperInvariant()));
        return "MasterSafe-" + Convert.ToHexString(hash, 0, 8);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _lock.Dispose();
    }
}
