// File:    AppServices.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Backup;

namespace MasterSafe.App.Services;

/// <summary>
/// The services the vault management and recovery screens need. When this is absent (design and test mode)
/// those screens show that they are not available.
/// </summary>
/// <param name="Paths">Where the app keeps its files.</param>
/// <param name="Catalog">The vault files and which one is in use.</param>
/// <param name="SettingsStore">Saved settings.</param>
/// <param name="Dialogs">File and folder pickers.</param>
/// <param name="RecoveryKeys">Creates and opens recovery key files.</param>
/// <param name="RecoveryBackups">Creates and reads recovery backups.</param>
public sealed record AppServices(
    AppPaths Paths,
    VaultCatalog Catalog,
    ISettingsStore SettingsStore,
    IFileDialogs Dialogs,
    RecoveryKeyService RecoveryKeys,
    RecipientBackupService RecoveryBackups);
