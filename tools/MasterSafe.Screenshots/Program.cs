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
//   dotnet run --project tools/MasterSafe.Screenshots -- <output folder> [--scene store|tour|snapshots|manual] [--data-folder <new folder>]
// "manual" draws the pages of the user manual. "store" draws the store screenshots. "tour" draws the pages of the getting-started article. "snapshots" draws the pages
// of the snapshots article and also does the restore described there with real file copies. --data-folder shows that path on the Vaults page (for example a Linux home
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

if (scene is not ("store" or "tour" or "snapshots" or "manual") || (dataFolderArgument is not null && Directory.Exists(dataFolderArgument)))
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
    if (scene == "tour")
    {
        await TourAsync(output, root, rootIsDataFolder);
        return;
    }

    if (scene == "manual")
    {
        await ManualAsync(output, root, rootIsDataFolder);
        return;
    }

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

// The pages of the user manual on the website: recovery, restoring, daily copies, folders and the help.
static async Task ManualAsync(string output, string root, bool rootIsDataFolder)
{
    // Use --data-folder so the Vaults page shows a neutral path instead of the temporary folder of this computer.
    var dataFolder = rootIsDataFolder ? root : Path.Combine(root, "data");
    var dialogs = new SaveIntoFolder(Path.Combine(dataFolder, "saved"));
    var main = Build(dataFolder, dialogs);
    const string password = "demo master password for screenshots";
    var contents = Demo.Contents();
    contents = contents with
    {
        Passwords =
        [
            .. contents.Passwords,
            new PasswordEntry { Title = "Team wiki", UserName = "alex", Password = PasswordGenerator.Generate(18), Folder = "Work/Projects" },
            new PasswordEntry { Title = "Travel booking", UserName = "alex@example.org", Password = PasswordGenerator.Generate(18), Folder = "Work/Travel" },
            new PasswordEntry { Title = "Old intranet", UserName = "alex", Password = PasswordGenerator.Generate(18), Folder = "Work/Archive/2024" },
        ],
    };
    await main.Vault.CreateAsync(password);
    await main.Vault.SaveContentsAsync(password, contents);

    // Signed out: the sign-in screen, and the help opened from it with F1.
    var locked = new MainWindowViewModel(main.Vault, services: main.Services);
    locked.Login.IsCreateMode = false;
    var lockedWindow = new MainWindow { DataContext = locked, Width = 1280, Height = 800 };
    lockedWindow.Show();
    await Shoot(lockedWindow, locked, locked.Login, Path.Combine(output, "manual-signin.png"));
    locked.Help.SelectedTopic = locked.Help.Topics.FirstOrDefault(t => t.Title.Contains("forgot", StringComparison.OrdinalIgnoreCase)) ?? locked.Help.SelectedTopic;
    await Shoot(lockedWindow, locked, locked.Help, Path.Combine(output, "manual-help.png"));

    // "Forgot your master password?" with the backup chosen and the key rebuilt from two parts.
    locked.Login.Forgot.IsOpen = true;
    locked.Login.Forgot.ShowRestore = true;
    locked.Login.Forgot.BackupFilePath = @"E:\MasterSafe\recovery-20261122-101500.sauth";
    locked.Login.Forgot.KeyPartsStatus = "The recovery key was rebuilt from its parts.";
    locked.Login.Forgot.KeyPassphrase = PasswordGenerator.Generate(28);
    locked.Login.Forgot.NewPassword = locked.Login.Forgot.ConfirmPassword = PasswordGenerator.Generate(20);
    lockedWindow.Height = 1180;
    await Shoot(lockedWindow, locked, locked.Login, Path.Combine(output, "manual-restore.png"));
    lockedWindow.Close();

    // Unlocked: a recovery key is set up the way a user does it, so the page shows what comes after.
    var shell = new MainWindowViewModel(main.Vault, services: main.Services);
    await shell.UnlockVaultAsync(password);
    shell.Settings.ShowReminders = false;
    shell.RefreshNotices();
    shell.Recovery.Passphrase = shell.Recovery.ConfirmPassphrase = PasswordGenerator.Generate(28);
    await shell.Recovery.CreateKeyCommand.ExecuteAsync(null);
    if (!shell.Recovery.HasKey)
    {
        throw new InvalidOperationException("The recovery key was not set up: " + shell.Recovery.Status);
    }

    shell.Recovery.Status = null;
    var window = new MainWindow { DataContext = shell, Width = 1280, Height = 1220 };
    window.Show();
    await Shoot(window, shell, shell.Recovery, Path.Combine(output, "manual-recovery.png"));

    shell.ShowVaultsCommand.Execute(null);
    window.Height = 1000;
    await Shoot(window, shell, shell.Vaults, Path.Combine(output, "manual-daily-copies.png"));

    window.Height = 800;
    shell.Passwords.ToggleFolderCommand.Execute(shell.Passwords.FolderNodes.Single(n => n.Path == "Work/Archive"));
    await Shoot(window, shell, shell.Passwords, Path.Combine(output, "manual-folders.png"));

    shell.ShowHealthCommand.Execute(null);
    await Shoot(window, shell, shell.Health, Path.Combine(output, "manual-health.png"));
    window.Close();
}

static (AppPaths Paths, AppServices Services, VaultService Vault) Build(string dataFolder, IFileDialogs? dialogs = null)
{
    var paths = new AppPaths(dataFolder);
    Directory.CreateDirectory(paths.DataFolder);
    var settings = new SettingsStore(paths.SettingsPath);
    var repository = new SwitchableVaultRepository(paths.DefaultVaultPath);
    var fast = new Argon2Parameters { MemorySizeKib = 8192, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };
    var hybrid = MasterSafe.PostQuantum.PostQuantumProviderFactory.CreateHybrid();
    var services = new AppServices(
        paths, new VaultCatalog(paths, repository, settings), settings, dialogs ?? new NoDialogs(),
        new RecoveryKeyService(new AesGcmEncryptionService(), new MasterPasswordService(), hybrid, fast),
        new RecipientBackupService(new AesGcmEncryptionService(), hybrid));
    return (paths, services, new VaultService(repository, new AesGcmEncryptionService(), new MasterPasswordService(), fast));
}

// The pages of the getting-started article, in the order a new user meets them. Nothing is typed into a real vault: the
// first-start screen belongs to a vault that does not exist yet, and the rest use the demo vault.
static async Task TourAsync(string output, string root, bool rootIsDataFolder)
{
    // 1. The very first start: no vault yet, so the app asks for a master password twice.
    var first = Build(Path.Combine(root, "first-run"));
    var firstShell = new MainWindowViewModel(first.Vault, services: first.Services);
    var typed = PasswordGenerator.Generate(24);
    firstShell.Login.IsCreateMode = true;
    firstShell.Login.MasterPassword = typed;
    firstShell.Login.ConfirmPassword = typed;
    var firstWindow = new MainWindow { DataContext = firstShell, Width = 1280, Height = 800 };
    firstWindow.Show();
    await Shoot(firstWindow, firstShell, firstShell.Login, Path.Combine(output, "tour-1-create.png"));
    firstWindow.Close();

    // 2. Every later start: the same screen asks for the password once.
    var main = Build(rootIsDataFolder ? root : Path.Combine(root, "data"));
    const string password = "demo master password for screenshots";
    await main.Vault.CreateAsync(password);
    await main.Vault.SaveContentsAsync(password, Demo.Contents());
    var signIn = new MainWindowViewModel(main.Vault, services: main.Services);
    signIn.Login.IsCreateMode = false;
    signIn.Login.MasterPassword = typed;
    var signInWindow = new MainWindow { DataContext = signIn, Width = 1280, Height = 800 };
    signInWindow.Show();
    await Shoot(signInWindow, signIn, signIn.Login, Path.Combine(output, "tour-2-signin.png"));
    signInWindow.Close();

    // 3. Unlocked.
    var shell = new MainWindowViewModel(main.Vault, services: main.Services);
    await shell.UnlockVaultAsync(password);
    shell.Dashboard.ResetNotices();
    shell.Dashboard.SetNotices([]);
    var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
    window.Show();

    shell.AccountEditor.Issuer = "Example Cloud";
    shell.AccountEditor.Label = "alex@example.com";
    shell.AccountEditor.Secret = Demo.NewKey();
    await Shoot(window, shell, shell.AccountEditor, Path.Combine(output, "tour-3-add-account.png"));
    await Shoot(window, shell, shell.Import, Path.Combine(output, "tour-4-import.png"));
    // A real first-day vault would show reminders here; the tour is about the codes.
    shell.Settings.ShowReminders = false;
    shell.RefreshNotices();
    await Shoot(window, shell, shell.Dashboard, Path.Combine(output, "tour-5-codes.png"));

    shell.Passwords.Title = "Example Cloud";
    shell.Passwords.UserName = "alex@example.com";
    shell.Passwords.Password = PasswordGenerator.Generate(20);
    shell.Passwords.Url = "https://cloud.example.com";
    await Shoot(window, shell, shell.Passwords, Path.Combine(output, "tour-6-add-password.png"));
    window.Close();
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

    public static string NewKey() => Key(9);

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

// Saves go into a folder of the run, which is deleted at the end; nothing is opened.
internal sealed class SaveIntoFolder(string folder) : IFileDialogs
{
    public Task<string?> OpenFileAsync(string title, string description, params string[] patterns) => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string? startFolder = null)
    {
        Directory.CreateDirectory(folder);
        return Task.FromResult<string?>(Path.Combine(folder, $"{suggestedName}.{extension}"));
    }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);

    public void ShowInFolder(string path)
    {
    }
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
