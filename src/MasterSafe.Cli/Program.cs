// File:    Program.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;

// Release signing and verification tools (ML-DSA-65, post-quantum):
//   MasterSafe.Cli verify-release <folder> <name>.pub   checks every installer and package in a folder
//   MasterSafe.Cli verify <file> <name>.pub             checks <file>.sig for one file
//   MasterSafe.Cli fingerprint <name>.pub               prints the SHA-256 fingerprint of a public key
//   MasterSafe.Cli keygen <name>                        writes <name>.pub and <name>.key (keep the .key file secret)
//   MasterSafe.Cli sign <file> <name>.key               writes <file>.sig
if (args.Length > 0 && args[0] is "keygen" or "sign" or "verify" or "verify-release" or "fingerprint" or "help" or "--help" or "-h")
{
    return RunSigningCommand(args);
}

static int RunSigningCommand(string[] args)
{
    if (!ReleaseSigner.IsSupported)
    {
        Console.Error.WriteLine("This operating system does not provide ML-DSA, which these commands need.");
        return 2;
    }

    try
    {
        return args[0] switch
        {
            "keygen" when args.Length == 2 => KeyGen(args[1]),
            "sign" when args.Length == 3 => Sign(args[1], args[2]),
            "verify" when args.Length == 3 => Verify(args[1], args[2]),
            "verify-release" when args.Length == 3 => VerifyRelease(args[1], args[2]),
            "fingerprint" when args.Length == 2 => Fingerprint(args[1]),
            "help" or "--help" or "-h" => Usage(0),
            _ => Usage(64),
        };
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: file not found: {ex.FileName}");
        Console.Error.WriteLine("Check the path. A .sig file must be in the same folder as the file it signs.");
        return 66;
    }
    catch (DirectoryNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 66;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Security.Cryptography.CryptographicException)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static int Usage(int exitCode)
{
    // Asked for help: print normally. Wrong arguments: print as an error.
    var output = exitCode == 0 ? Console.Out : Console.Error;
    output.WriteLine("MasterSafe.Cli, release verification and signing");
    output.WriteLine("  verify-release <folder> <pubkey>   check every installer and package in a downloaded release folder");
    output.WriteLine("  verify <file> <pubkey>             check one file against its .sig");
    output.WriteLine("  fingerprint <pubkey>               print the public key fingerprint");
    output.WriteLine("  keygen <name>                      create a signing key pair (maintainers)");
    output.WriteLine("  sign <file> <key>                  sign a file (maintainers)");
    return exitCode;
}

static byte[] ReadKey(string path) => Convert.FromBase64String(File.ReadAllText(path).Trim());

static string FingerprintOf(byte[] publicKey)
    => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(publicKey)).ToLowerInvariant();

static int KeyGen(string name)
{
    var (publicKey, seed) = ReleaseSigner.GenerateKeyPair();
    File.WriteAllText(name + ".pub", Convert.ToBase64String(publicKey));
    File.WriteAllText(name + ".key", Convert.ToBase64String(seed));
    Console.WriteLine($"Wrote {name}.pub (publish this) and {name}.key (keep secret).");
    return 0;
}

static int Sign(string file, string keyFile)
{
    var signature = ReleaseSigner.Sign(File.ReadAllBytes(file), ReadKey(keyFile));
    File.WriteAllText(file + ".sig", Convert.ToBase64String(signature));
    Console.WriteLine($"Wrote {file}.sig");
    return 0;
}

static int Verify(string file, string pubFile)
{
    var ok = ReleaseSigner.Verify(File.ReadAllBytes(file), Convert.FromBase64String(File.ReadAllText(file + ".sig").Trim()), ReadKey(pubFile));
    Console.WriteLine(ok ? "Signature is valid." : "SIGNATURE IS NOT VALID.");
    return ok ? 0 : 1;
}

static int Fingerprint(string pubFile)
{
    Console.WriteLine(FingerprintOf(ReadKey(pubFile)));
    return 0;
}

static int VerifyRelease(string folder, string pubFile)
{
    var publicKey = ReadKey(pubFile);
    Console.WriteLine($"Public key fingerprint: {FingerprintOf(publicKey)}");
    Console.WriteLine("Compare it with the fingerprint in the project README and SECURITY.md.");
    Console.WriteLine();

    var results = ReleaseVerifier.VerifyFolder(folder, publicKey);
    if (results.Count == 0)
    {
        Console.Error.WriteLine("No installer or package files were found in that folder.");
        return 1;
    }

    foreach (var r in results)
    {
        var checksum = r.ChecksumOk switch { true => "checksum ok", false => "CHECKSUM DIFFERS", _ => "no checksum file" };
        var signature = r.SignatureOk switch { true => "signature valid", false => "SIGNATURE NOT VALID", _ => "NOT SIGNED" };
        Console.WriteLine($"{(r.Passed ? "OK  " : "FAIL")}  {r.FileName}: {signature}, {checksum}");
        if (!r.Passed && r.Note is not null)
        {
            Console.WriteLine($"      {r.Note}");
        }
    }

    var failed = results.Count(r => !r.Passed);
    Console.WriteLine();
    Console.WriteLine(failed == 0
        ? $"All {results.Count} files are genuine."
        : $"{failed} of {results.Count} files failed. Do not install or run them.");
    return failed == 0 ? 0 : 1;
}

Console.WriteLine("MasterSafe Authenticator CLI demo (.NET 10)");
Console.WriteLine("=====================================");

// 1. Parse an otpauth URI into an account.
const string uri = "otpauth://totp/GitHub:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA1&digits=6&period=30";
var account = OtpAuthParser.Parse(uri);
Console.WriteLine($"Imported: {account.Issuer} ({account.Label})");

// 2. Generate the live TOTP code.
var now = DateTimeOffset.UtcNow;
Console.WriteLine($"Current code: {account.GenerateCode(now)}  (valid {TotpGenerator.RemainingSeconds(now, account.Period)}s)");

// 3. Create an encrypted vault in a temp file, save and re-open it.
var vaultPath = Path.Combine(Path.GetTempPath(), $"sa-demo-{Guid.NewGuid():N}.sauth");
var vault = new VaultService(
    new FileVaultRepository(vaultPath),
    new AesGcmEncryptionService(),
    new MasterPasswordService());

await vault.SaveAsync("demo-master-password", [account]);
Console.WriteLine($"Vault written (encrypted) to: {vaultPath}");

var loaded = await vault.OpenAsync("demo-master-password");
Console.WriteLine($"Vault opened: {loaded.Count} account(s) decrypted.");

try
{
    await vault.OpenAsync("wrong-password");
}
catch (InvalidPasswordException)
{
    Console.WriteLine("Wrong password correctly rejected by AES-256-GCM authentication.");
}

File.Delete(vaultPath);
Console.WriteLine("Done.");

return 0;
