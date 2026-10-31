// File:    Program.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Core;
using MasterSafe.Crypto;
using MasterSafe.PostQuantum;

// Release signing tools (ML-DSA-65, post-quantum):
//   MasterSafe.Cli keygen <name>             writes <name>.pub and <name>.key (keep the .key file secret)
//   MasterSafe.Cli sign <file> <name>.key    writes <file>.sig
//   MasterSafe.Cli verify <file> <name>.pub  checks <file>.sig
if (args.Length > 0 && args[0] is "keygen" or "sign" or "verify")
{
    return RunSigningCommand(args);
}

static int RunSigningCommand(string[] args)
{
    if (!ReleaseSigner.IsSupported)
    {
        Console.Error.WriteLine("This operating system does not provide ML-DSA.");
        return 2;
    }

    switch (args[0])
    {
        case "keygen" when args.Length == 2:
        {
            var (publicKey, seed) = ReleaseSigner.GenerateKeyPair();
            File.WriteAllText(args[1] + ".pub", Convert.ToBase64String(publicKey));
            File.WriteAllText(args[1] + ".key", Convert.ToBase64String(seed));
            Console.WriteLine($"Wrote {args[1]}.pub (publish this) and {args[1]}.key (keep secret).");
            return 0;
        }

        case "sign" when args.Length == 3:
        {
            var signature = ReleaseSigner.Sign(File.ReadAllBytes(args[1]), Convert.FromBase64String(File.ReadAllText(args[2]).Trim()));
            File.WriteAllText(args[1] + ".sig", Convert.ToBase64String(signature));
            Console.WriteLine($"Wrote {args[1]}.sig");
            return 0;
        }

        case "verify" when args.Length == 3:
        {
            var ok = ReleaseSigner.Verify(
                File.ReadAllBytes(args[1]),
                Convert.FromBase64String(File.ReadAllText(args[1] + ".sig").Trim()),
                Convert.FromBase64String(File.ReadAllText(args[2]).Trim()));
            Console.WriteLine(ok ? "Signature is valid." : "SIGNATURE IS NOT VALID.");
            return ok ? 0 : 1;
        }

        default:
            Console.Error.WriteLine("Usage: keygen <name> | sign <file> <key> | verify <file> <pubkey>");
            return 64;
    }
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
