// File:    ReleaseKeyTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>
/// Guards the published release key: it must be a real ML-DSA-65 public key (never a private key by mistake), and
/// the fingerprint printed in the README and SECURITY.md must match the file.
/// </summary>
public class ReleaseKeyTests
{
    private static string? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MasterSafe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static byte[]? PublicKeyBytes()
    {
        var root = RepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "signing", "masterSafe-release.pub");
        return path is not null && File.Exists(path) ? Convert.FromBase64String(File.ReadAllText(path).Trim()) : null;
    }

    [Fact]
    public void PublishedKey_IsAnMlDsa65PublicKey()
    {
        var bytes = PublicKeyBytes();
        if (bytes is null || !MLDsa.IsSupported)
        {
            return;
        }

        // A public key is 1952 bytes. A 32-byte file would be the private seed, which must never be committed.
        bytes.Should().HaveCount(MLDsaAlgorithm.MLDsa65.PublicKeySizeInBytes);
        var import = () => MLDsa.ImportMLDsaPublicKey(MLDsaAlgorithm.MLDsa65, bytes).Dispose();
        import.Should().NotThrow();
    }

    [Fact]
    public void PublishedKey_RejectsAnUnrelatedSignature()
    {
        var bytes = PublicKeyBytes();
        if (bytes is null || !ReleaseSigner.IsSupported)
        {
            return;
        }

        var (_, otherSeed) = ReleaseSigner.GenerateKeyPair();
        var data = "installer"u8.ToArray();
        var foreignSignature = ReleaseSigner.Sign(data, otherSeed);

        ReleaseSigner.Verify(data, foreignSignature, bytes).Should().BeFalse();
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SECURITY.md")]
    public void DocumentedFingerprint_MatchesTheKeyFile(string document)
    {
        var bytes = PublicKeyBytes();
        var root = RepositoryRoot();
        if (bytes is null || root is null)
        {
            return;
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var text = File.ReadAllText(Path.Combine(root, document));

        Regex.IsMatch(text, "[0-9a-f]{64}").Should().BeTrue();
        text.Should().Contain(fingerprint);
    }

    [Fact]
    public void NoPrivateKeyFilesAreInTheSigningFolder()
    {
        var root = RepositoryRoot();
        if (root is null || !Directory.Exists(Path.Combine(root, "signing")))
        {
            return;
        }

        Directory.GetFiles(Path.Combine(root, "signing"))
            .Select(Path.GetFileName)
            .Should().OnlyContain(name => name!.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
    }
}
