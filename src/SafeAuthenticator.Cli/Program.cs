using SafeAuthenticator.Core;
using SafeAuthenticator.Crypto;

Console.WriteLine("SafeAuthenticator CLI demo (.NET 10)");
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
