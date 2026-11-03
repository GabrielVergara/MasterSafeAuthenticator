// File:    ReleaseVerifier.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text.Json;

namespace MasterSafe.PostQuantum;

/// <summary>The result of checking one release file.</summary>
/// <param name="FileName">File name inside the release folder.</param>
/// <param name="ChecksumOk">True or false when a <c>.sha256</c> file exists, otherwise null.</param>
/// <param name="SignatureOk">True or false when a <c>.sig</c> file exists, otherwise null.</param>
/// <param name="Note">A short explanation when something is wrong or missing.</param>
public sealed record ReleaseFileResult(string FileName, bool? ChecksumOk, bool? SignatureOk, string? Note)
{
    /// <summary>The file is acceptable: signed correctly, and its checksum matches when one was supplied.</summary>
    public bool Passed => SignatureOk == true && ChecksumOk != false;
}

/// <summary>One file listed in a release manifest.</summary>
public sealed record ReleaseManifestFile(string Name, string Sha256);

/// <summary>
/// The signed list of a release: its version and the name and SHA-256 of each file. A signature on an installer alone says
/// nothing about which version it is, so an old signed installer could be offered as a new one. This list binds them.
/// </summary>
public sealed record ReleaseManifest(string Product, string Version, IReadOnlyList<ReleaseManifestFile> Files)
{
    /// <summary>The file name of the manifest in a release.</summary>
    public const string FileName = "MasterSafe-release-manifest.json";

    /// <summary>The product name the manifest must carry.</summary>
    public const string ProductName = "MasterSafe Authenticator";

    /// <summary>Largest manifest that is read.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>Writes the manifest for the named files in <paramref name="folder"/>.</summary>
    public static byte[] Build(string folder, string version, IEnumerable<string> fileNames)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentNullException.ThrowIfNull(fileNames);

        var files = fileNames
            .Order(StringComparer.Ordinal)
            .Select(name => new ReleaseManifestFile(name, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, name)))).ToLowerInvariant()))
            .ToList();
        return JsonSerializer.SerializeToUtf8Bytes(new ReleaseManifest(ProductName, version, files), new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads a manifest. Null when it is not one.</summary>
    public static ReleaseManifest? TryParse(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxBytes)
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json);
            return manifest is { Product: ProductName, Version: { Length: > 0 }, Files: not null } ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The recorded SHA-256 of a file, or null when the manifest does not list it.</summary>
    public string? HashOf(string fileName) => Files.FirstOrDefault(f => f.Name == fileName)?.Sha256;
}

/// <summary>
/// Checks a folder of downloaded release files against the project's public key: every installer or package must
/// have a valid ML-DSA-65 signature, and its SHA-256 must match the <c>.sha256</c> file next to it.
/// </summary>
public static class ReleaseVerifier
{
    private static readonly string[] ReleaseExtensions = [".exe", ".msi", ".zip", ".gz", ".deb", ".rpm", ".appimage", ".dmg"];

    /// <summary>Verifies every release file in <paramref name="folder"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static IReadOnlyList<ReleaseFileResult> VerifyFolder(string folder, byte[] publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"The folder '{folder}' does not exist.");
        }

        var results = new List<ReleaseFileResult>();
        foreach (var path in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) || !IsReleaseFile(path))
            {
                continue;
            }

            results.Add(VerifyFile(path, publicKey));
        }

        return results;
    }

    /// <summary>
    /// Checks the signed manifest in <paramref name="folder"/> against the key and every file it lists. Returns the problem, or
    /// null when all is well. The version of the manifest is returned through <paramref name="version"/>.
    /// </summary>
    public static string? VerifyManifest(string folder, byte[] publicKey, out string? version)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(publicKey);

        version = null;
        var path = Path.Combine(folder, ReleaseManifest.FileName);
        if (!File.Exists(path) || !File.Exists(path + ".sig"))
        {
            return "the release has no signed manifest";
        }

        var bytes = File.ReadAllBytes(path);
        try
        {
            if (!ReleaseSigner.Verify(bytes, Convert.FromBase64String(File.ReadAllText(path + ".sig").Trim()), publicKey))
            {
                return "the manifest signature is not valid";
            }
        }
        catch (FormatException)
        {
            return "the manifest signature is damaged";
        }

        var manifest = ReleaseManifest.TryParse(bytes);
        if (manifest is null)
        {
            return "the manifest is damaged";
        }

        version = manifest.Version;
        foreach (var file in manifest.Files)
        {
            var target = Path.Combine(folder, Path.GetFileName(file.Name));
            if (!File.Exists(target))
            {
                return $"{file.Name} is listed in the manifest but missing";
            }

            if (!string.Equals(file.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))), StringComparison.OrdinalIgnoreCase))
            {
                return $"{file.Name} differs from the manifest";
            }
        }

        return null;
    }

    /// <summary>Verifies a single file using the <c>.sig</c> and <c>.sha256</c> files next to it.</summary>
    public static ReleaseFileResult VerifyFile(string path, byte[] publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(publicKey);

        var name = Path.GetFileName(path);
        var data = File.ReadAllBytes(path);
        var notes = new List<string>();

        bool? checksumOk = null;
        var checksumPath = path + ".sha256";
        if (File.Exists(checksumPath))
        {
            var recorded = File.ReadAllText(checksumPath).Trim().Split(' ', '\t', '*')[0];
            checksumOk = string.Equals(recorded, Convert.ToHexString(SHA256.HashData(data)), StringComparison.OrdinalIgnoreCase);
            if (checksumOk == false)
            {
                notes.Add("the SHA-256 does not match the .sha256 file");
            }
        }
        else
        {
            notes.Add("no .sha256 file to compare with");
        }

        bool? signatureOk = null;
        var signaturePath = path + ".sig";
        if (File.Exists(signaturePath))
        {
            try
            {
                signatureOk = ReleaseSigner.Verify(data, Convert.FromBase64String(File.ReadAllText(signaturePath).Trim()), publicKey);
                if (signatureOk == false)
                {
                    notes.Add("the signature is not valid for this file and key");
                }
            }
            catch (FormatException)
            {
                signatureOk = false;
                notes.Add("the .sig file is damaged");
            }
        }
        else
        {
            notes.Add("no .sig file: this file is not signed");
        }

        return new ReleaseFileResult(name, checksumOk, signatureOk, notes.Count == 0 ? null : string.Join("; ", notes));
    }

    // A file is a release file when it looks like an installer or package, or when it has a signature or checksum
    // next to it. The second rule covers programs without an extension, such as the Linux command-line tool.
    private static bool IsReleaseFile(string path)
        => ReleaseExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))
           || File.Exists(path + ".sig")
           || File.Exists(path + ".sha256");
}
