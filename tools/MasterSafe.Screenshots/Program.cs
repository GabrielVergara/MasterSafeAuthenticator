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
//   dotnet run --project tools/MasterSafe.Screenshots -- <output folder> [--scene store|snapshots] [--data-folder <new folder>]
// "store" draws the store screenshots. "snapshots" draws the pages of the snapshots article and also does the restore
// described there with real file copies. --data-folder shows that path on the Vaults page (for example a Linux home
// folder in a container); it must not exist yet, because the tool deletes it at the end.
string? outputArgument = null, dataFolderArgument = null;
var scene = "store";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scene" when i + 1 < args.Length:
            scene = args[++i];
            break;
        case "--data-folder" when i + 1 < args.Length:
            dataFolderArgument = Path.GetFullPath(args[++i]);
            break;
        default:
            outputArgument ??= args[i];
            break;
    }
}

if (scene is not ("store" or "snapshots") || (dataFolderArgument is not null && Directory.Exists(dataFolderArgument)))
{
    Console.Error.WriteLine("Unknown scene, or the data folder already exists (it is deleted at the end, so it must be new).");
    return 1;
}

var output = Path.GetFullPath(outputArgument ?? "docs/screenshots");
Directory.CreateDirectory(output);
var root = dataFolderArgument ?? Path.Combine(Path.GetTempPath(), "ms-shots-" + Guid.NewGuid().ToString("N"));

AppBuilder.Configure<MasterSafe.App.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

try
{
    var task = Dispatcher.UIThread.InvokeAsync(() => DrawAsync(output, root, dataFolderArgument is not null, scene));
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

static async Task DrawAsync(string output, string root, bool rootIsDataFolder, string scene)
{
    var paths = new AppPaths(rootIsDataFolder ? root : Path.Combine(root, "data"));
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

    if (scene == "store")
    {
        await Shoot(window, shell, shell.Dashboard, Path.Combine(output, "codes.png"));
        await Shoot(window, shell, shell.Passwords, Path.Combine(output, "passwords.png"));
        await Shoot(window, shell, shell.Settings, Path.Combine(output, "settings.png"));
        window.Close();
        return;
    }

    await Shoot(window, shell, shell.Vaults, Path.Combine(output, "snapshots-vaults.png"));
    await Shoot(window, shell, shell.Backup, Path.Combine(output, "snapshots-backup.png"));
    window.Close();
    await RestoreAsync(output, paths, services, vault, password);
}

// The restore of the snapshots article, with real files: copy the vault as a snapshot, make a "mistake" (delete an
// account), then rename vault.sauth, copy the snapshot over it and open it. The save counter kept away from the vault is
// raised as a later save would have done, which is what makes the app say the vault is an older copy.
static async Task RestoreAsync(string output, AppPaths paths, AppServices services, VaultService vault, string password)
{
    var snapshots = Path.Combine(paths.DataFolder, "snapshots");
    Directory.CreateDirectory(snapshots);
    var snapshot = Path.Combine(snapshots, $"vault-{DateTime.UtcNow.AddDays(-1):yyyyMMdd}.sauth");
    File.Copy(paths.DefaultVaultPath, snapshot);

    var contents = await vault.OpenContentsAsync(password);
    await vault.SaveContentsAsync(password, contents with { Accounts = contents.Accounts.Skip(1).ToList() });
    var id = await vault.GetVaultIdAsync() ?? throw new InvalidOperationException("No vault id.");
    var counters = new SaveCounterStore(paths.SaveCounterPath);
    counters.Set(id, Math.Max(counters.Get(id), contents.SaveCounter) + 8);

    File.Move(paths.DefaultVaultPath, paths.DefaultVaultPath + ".before-restore");
    File.Copy(snapshot, paths.DefaultVaultPath);

    var shell = new MainWindowViewModel(vault, services: services);
    await shell.UnlockVaultAsync(password);
    shell.Settings.ShowReminders = false;
    shell.RefreshNotices();
    var restored = shell.Dashboard.Accounts.Count;
    var notice = shell.Dashboard.Notices.Any(n => n.Key == "older-copy");
    Console.WriteLine($"Restore check: unlocked={shell.IsUnlocked}, accounts={restored} (the snapshot had {contents.Accounts.Count}), older-copy notice={notice}");
    if (!shell.IsUnlocked || restored != contents.Accounts.Count || !notice)
    {
        throw new InvalidOperationException("The restore did not behave as the article says.");
    }

    var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
    window.Show();
    await Shoot(window, shell, shell.Dashboard, Path.Combine(output, "snapshots-restored.png"));
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
