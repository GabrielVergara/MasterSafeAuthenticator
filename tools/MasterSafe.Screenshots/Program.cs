// File:    Program.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using MasterSafe.App.Services;
using MasterSafe.App.ViewModels;
using MasterSafe.App.Views;
using MasterSafe.Backup;
using MasterSafe.Core;
using MasterSafe.Crypto;

// The real app blocks screen capture, so the store screenshots are drawn here instead: the real windows and pages,
// rendered by Skia without a screen, from a demo vault that exists only in a temporary folder. The user's vault is
// never opened.
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/screenshots");
Directory.CreateDirectory(output);
var root = Path.Combine(Path.GetTempPath(), "ms-shots-" + Guid.NewGuid().ToString("N"));

AppBuilder.Configure<MasterSafe.App.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

try
{
    var task = Dispatcher.UIThread.InvokeAsync(() => DrawAsync(output, root));
    while (!task.IsCompleted)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }

    task.GetAwaiter().GetResult();
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch (IOException)
    {
    }
}

return 0;

static async Task DrawAsync(string output, string root)
{
    var paths = new AppPaths(Path.Combine(root, "data"));
    Directory.CreateDirectory(paths.DataFolder);
    var settings = new SettingsStore(paths.SettingsPath);
    var repository = new SwitchableVaultRepository(paths.DefaultVaultPath);
    var fast = new Argon2Parameters { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };
    var hybrid = MasterSafe.PostQuantum.PostQuantumProviderFactory.CreateHybrid();
    var services = new AppServices(
        paths, new VaultCatalog(paths, repository, settings), settings, new NoDialogs(),
        new RecoveryKeyService(new AesGcmEncryptionService(), new MasterPasswordService(), hybrid, fast),
        new RecipientBackupService(new AesGcmEncryptionService(), hybrid));
    var vault = new VaultService(repository, new AesGcmEncryptionService(), new MasterPasswordService(), fast);

    const string password = "demo master password for screenshots";
    await vault.CreateAsync(password);
    await vault.SaveContentsAsync(password, Demo.Contents());

    var shell = new MainWindowViewModel(vault, services: services);
    await shell.UnlockVaultAsync(password);
    shell.Dashboard.ResetNotices();
    shell.Dashboard.SetNotices([]);

    var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
    window.Show();

    await Shoot(window, shell, shell.Dashboard, Path.Combine(output, "codes.png"));
    await Shoot(window, shell, shell.Passwords, Path.Combine(output, "passwords.png"));
    await Shoot(window, shell, shell.Settings, Path.Combine(output, "settings.png"));
    window.Close();
}

static async Task Shoot(MasterSafe.App.Views.MainWindow window, MainWindowViewModel shell, ViewModelBase page, string path)
{
    shell.CurrentPage = page;
    for (var i = 0; i < 20; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(20);
    }

    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was drawn.");
#pragma warning disable CS0618 // The PNG default is what is wanted.
    frame.Save(path);
#pragma warning restore CS0618
    Console.WriteLine($"Saved {path} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
}

internal static class Demo
{
    // Made up when the tool runs: no real account, and nothing in the source that looks like a key.
    private static string Key(int n) => Base32.Encode(System.Text.Encoding.ASCII.GetBytes($"demo-account-{n:D2}"));

    private static string Phrase(int n) => PasswordGenerator.Generate(16 + n);

    public static VaultContents Contents() => new()
    {
        Accounts =
        [
            new OtpAccount { Issuer = "GitHub", Label = "alex@example.com", Secret = Key(1) },
            new OtpAccount { Issuer = "Google", Label = "alex.demo@example.com", Secret = Key(2) },
            new OtpAccount { Issuer = "Microsoft", Label = "alex@example.org", Secret = Key(3) },
            new OtpAccount { Issuer = "Proton", Label = "alex@example.net", Secret = Key(4) },
            new OtpAccount { Issuer = "Dropbox", Label = "alex", Secret = Key(5) },
            new OtpAccount { Issuer = "Mastodon", Label = "@alex@example.social", Secret = Key(6) },
        ],
        Passwords =
        [
            new PasswordEntry { Title = "Example Mail", UserName = "alex@example.com", Password = Phrase(1), Url = "https://mail.example.com", Folder = "Email" },
            new PasswordEntry { Title = "Work mail", UserName = "alex@example.org", Password = Phrase(2), Url = "https://example.org", Folder = "Email" },
            new PasswordEntry { Title = "Example Bank", UserName = "alex-demo", Password = Phrase(3), Url = "https://bank.example.com", Folder = "Finance" },
            new PasswordEntry { Title = "Home Wi-Fi", Password = Phrase(4), Folder = "Home" },
            new PasswordEntry { Title = "Example Shop", UserName = "alex@example.com", Password = Phrase(5), Url = "https://shop.example.com" },
        ],
    };
}

internal sealed class NoDialogs : IFileDialogs
{
    public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string? startFolder = null) => Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public void ShowInFolder(string path)
    {
    }
}
