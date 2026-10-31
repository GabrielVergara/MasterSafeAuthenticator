// File:    ReleaseVerifier.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;

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
                || name.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) || !IsReleaseFile(name))
            {
                continue;
            }

            results.Add(VerifyFile(path, publicKey));
        }

        return results;
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

    private static bool IsReleaseFile(string name)
        => ReleaseExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
