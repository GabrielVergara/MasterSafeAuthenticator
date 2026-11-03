// File:    SaveCounterStore.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterSafe.App.Services;

/// <summary>
/// Remembers, outside the vault file, how many times each vault was saved. When the vault says it was saved fewer times than
/// this, it is an older copy. This is a warning and never a block: restoring a backup is normal. It notices a swapped
/// file. It cannot notice a program that changes this file as well, because any program of the same user can. The file holds
/// counts only; a vault's identifier is hashed, so nothing here can be linked to a vault without having it.
/// </summary>
public sealed class SaveCounterStore(string path)
{
    private const long MaxBytes = 256 * 1024;
    private const int MaxVaults = 200;
    private const long MaxCount = 1L << 50;

    /// <summary>The last count seen for a vault, or 0 when none is known.</summary>
    public long Get(string vaultId)
    {
        // This file can be edited by anyone. A value no real vault could reach is treated as unknown.
        var count = Read().GetValueOrDefault(Key(vaultId));
        return count is >= 0 and < MaxCount ? count : 0;
    }

    /// <summary>Records a count. A problem here must never stop a save, so it is ignored.</summary>
    public void Set(string vaultId, long count)
    {
        try
        {
            var counters = Read();
            counters[Key(vaultId)] = count;
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(folder);
                }
                else
                {
                    Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(counters.TakeLast(MaxVaults).ToDictionary(c => c.Key, c => c.Value)));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The vault itself is safe. Only the warning about an older copy is weaker until the next save.
        }
    }

    private Dictionary<string, long> Read()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes)
            {
                return [];
            }

            return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Key(string vaultId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(vaultId)), 0, 16);
}
