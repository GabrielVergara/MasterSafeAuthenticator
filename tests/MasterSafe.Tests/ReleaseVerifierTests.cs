// File:    ReleaseVerifierTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using FluentAssertions;
using MasterSafe.PostQuantum;
using Xunit;

namespace MasterSafe.Tests;

public sealed class ReleaseVerifierTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms-release-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _publicKey;
    private readonly byte[] _seed;

    public ReleaseVerifierTests()
    {
        Directory.CreateDirectory(_folder);
        if (ReleaseSigner.IsSupported)
        {
            (_publicKey, _seed) = ReleaseSigner.GenerateKeyPair();
        }
        else
        {
            _publicKey = [];
            _seed = [];
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private string AddSigned(string name, string content, bool checksum = true, bool signature = true)
    {
        var path = Path.Combine(_folder, name);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(path, bytes);
        if (checksum)
        {
            File.WriteAllText(path + ".sha256", $"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}  {name}");
        }

        if (signature)
        {
            File.WriteAllText(path + ".sig", Convert.ToBase64String(ReleaseSigner.Sign(bytes, _seed)));
        }

        return path;
    }

    [Fact]
    public void GenuineFiles_Pass()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        AddSigned("app-setup.exe", "installer");
        AddSigned("app-linux.tar.gz", "package");

        var results = ReleaseVerifier.VerifyFolder(_folder, _publicKey);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Passed && r.ChecksumOk == true && r.SignatureOk == true);
    }

    [Fact]
    public void ChangedFile_Fails_OnBothChecksumAndSignature()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var path = AddSigned("app-setup.exe", "installer");
        File.AppendAllText(path, "tampered");

        var result = ReleaseVerifier.VerifyFolder(_folder, _publicKey).Single();

        result.Passed.Should().BeFalse();
        result.ChecksumOk.Should().BeFalse();
        result.SignatureOk.Should().BeFalse();
    }

    [Fact]
    public void UnsignedFile_Fails()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        AddSigned("app-setup.exe", "installer", signature: false);

        var result = ReleaseVerifier.VerifyFolder(_folder, _publicKey).Single();

        result.Passed.Should().BeFalse();
        result.SignatureOk.Should().BeNull();
        result.Note.Should().Contain("not signed");
    }

    [Fact]
    public void FileSignedWithAnotherKey_Fails()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        AddSigned("app-setup.exe", "installer");
        var (otherPublic, _) = ReleaseSigner.GenerateKeyPair();

        var result = ReleaseVerifier.VerifyFolder(_folder, otherPublic).Single();

        result.Passed.Should().BeFalse();
        result.SignatureOk.Should().BeFalse();
    }

    [Fact]
    public void DamagedSignatureFile_FailsCleanly()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        var path = AddSigned("app-setup.exe", "installer");
        File.WriteAllText(path + ".sig", "this is not base64 !!");

        var result = ReleaseVerifier.VerifyFolder(_folder, _publicKey).Single();

        result.Passed.Should().BeFalse();
        result.Note.Should().Contain("damaged");
    }

    [Fact]
    public void MissingChecksumFile_IsNotAFailure_ButIsReported()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        AddSigned("app-setup.exe", "installer", checksum: false);

        var result = ReleaseVerifier.VerifyFolder(_folder, _publicKey).Single();

        result.Passed.Should().BeTrue();
        result.ChecksumOk.Should().BeNull();
        result.Note.Should().Contain("no .sha256");
    }

    [Fact]
    public void SignatureChecksumAndKeyFiles_AreNotTreatedAsReleaseFiles()
    {
        if (!ReleaseSigner.IsSupported)
        {
            return;
        }

        AddSigned("app-setup.exe", "installer");
        File.WriteAllText(Path.Combine(_folder, "masterSafe-release.pub"), Convert.ToBase64String(_publicKey));
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "hello");

        ReleaseVerifier.VerifyFolder(_folder, _publicKey).Should().ContainSingle().Which.FileName.Should().Be("app-setup.exe");
    }

    [Fact]
    public void MissingFolder_Throws()
    {
        var act = () => ReleaseVerifier.VerifyFolder(Path.Combine(_folder, "nope"), []);
        act.Should().Throw<DirectoryNotFoundException>();
    }
}
