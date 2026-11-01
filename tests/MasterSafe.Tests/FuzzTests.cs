// File:    FuzzTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using FluentAssertions;
using MasterSafe.Core;
using MasterSafe.Core.Import;
using MasterSafe.Crypto;
using Xunit;

namespace MasterSafe.Tests;

/// <summary>
/// Everything MasterSafe reads may come from someone else, so damaged input must be refused with a clear error, never crash
/// or hang the program. These tests take valid samples, damage them at random and feed them to the readers. This is mutation
/// testing rather than coverage-guided fuzzing. A short run is part of the normal tests; <c>fuzz.yml</c> runs a long one with
/// MASTERSAFE_FUZZ_ITERATIONS set. MASTERSAFE_FUZZ_SEED repeats a run that failed.
/// </summary>
[Trait("Category", "Fuzz")]
public sealed class FuzzTests
{
    private static readonly int Iterations = int.TryParse(Environment.GetEnvironmentVariable("MASTERSAFE_FUZZ_ITERATIONS"), out var n) ? n : 2000;

    private static readonly int Seed = int.TryParse(Environment.GetEnvironmentVariable("MASTERSAFE_FUZZ_SEED"), out var s) ? s : Environment.TickCount;

    private static readonly TimeSpan SlowestAccepted = TimeSpan.FromSeconds(10);

    private static readonly string[] Tokens =
    [
        "\0", "\"", "'", "<", ">", "&", "=", "%", "%00", "\\", "\n", "{", "}", "[", "]", ",", "-1", "0", "99999999999", "null", "true",
        "<!DOCTYPE x [<!ENTITY a \"b\">]>", "&a;", "<![CDATA[", "otpauth://", "secret=", "digits=", "period=", "algorithm=", "counter=",
    ];

    /// <summary>The refusals a reader may answer with. Anything else (a null reference, an index out of range) is a bug.</summary>
    private static bool IsCleanRefusal(Exception ex) => ex is FormatException or InvalidDataException or KeePassImportException
        or JsonException or XmlException or CryptographicException or ArgumentException or InvalidPasswordException;

    private static byte[] Damage(byte[] sample, Random random)
    {
        var data = new List<byte>(sample);
        for (var i = random.Next(1, 6); i > 0; i--)
        {
            var at = data.Count == 0 ? 0 : random.Next(data.Count);
            switch (random.Next(6))
            {
                case 0 when data.Count > 0:
                    data[at] ^= (byte)(1 << random.Next(8));
                    break;
                case 1 when data.Count > 0:
                    data[at] = (byte)random.Next(256);
                    break;
                case 2:
                    data.Insert(at, (byte)random.Next(256));
                    break;
                case 3 when data.Count > 0:
                    data.RemoveRange(at, Math.Min(random.Next(1, 64), data.Count - at));
                    break;
                case 4 when data.Count > 0:
                    data.InsertRange(at, data.GetRange(at, Math.Min(random.Next(1, 64), data.Count - at)));
                    break;
                default:
                    data.InsertRange(at, Encoding.UTF8.GetBytes(Tokens[random.Next(Tokens.Length)]));
                    break;
            }
        }

        return [.. data];
    }

    private static void Run(string name, byte[] sample, Action<byte[]> read, int iterations)
    {
        var random = new Random(Seed);
        for (var i = 0; i < iterations; i++)
        {
            var input = Damage(sample, random);
            var watch = Stopwatch.StartNew();
            try
            {
                read(input);
            }
            catch (Exception ex) when (IsCleanRefusal(ex))
            {
                // Refused with a clear error: exactly what should happen.
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{name}: seed {Seed}, round {i}: {ex.GetType().Name} on input {Convert.ToBase64String(input)}", ex);
            }

            watch.Elapsed.Should().BeLessThan(SlowestAccepted, $"{name} must not hang (seed {Seed}, round {i})");
        }
    }

    private static string Text(byte[] data) => Encoding.UTF8.GetString(data);

    [Fact]
    public void Base32_RefusesDamagedKeys()
        => Run("Base32", "JBSWY3DPEHPK3PXP"u8.ToArray(), d => Base32.Decode(Text(d)), Iterations);

    [Fact]
    public void SetupLinks_AreRefusedWhenDamaged()
    {
        var link = "otpauth://totp/GitHub:a%40b.c?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA256&digits=8&period=30"u8.ToArray();
        Run("OtpAuthParser", link, d => OtpAuthParser.Parse(Text(d)), Iterations);
        Run("OtpAuthParser.TryParse", link, d => OtpAuthParser.TryParse(Text(d), out _), Iterations);
    }

    [Fact]
    public void KeePassExports_AreRefusedWhenDamaged()
    {
        const string Xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <KeePassFile><Meta><Binaries><Binary ID="0" Compressed="False">aGVsbG8=</Binary></Binaries></Meta>
            <Root><Group><Name>Root</Name><Group><Name>Mail</Name>
            <Entry><String><Key>Title</Key><Value>Mail</Value></String><String><Key>Password</Key><Value Protected="False">p@ss</Value></String>
            <String><Key>otp</Key><Value>otpauth://totp/x?secret=JBSWY3DPEHPK3PXP</Value></String>
            <Binary><Key>a.txt</Key><Value Ref="0" /></Binary>
            <History><Entry><String><Key>Title</Key><Value>Old</Value></String></Entry></History></Entry>
            </Group></Group></Root></KeePassFile>
            """;
        const string Csv = "\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\",\"TOTP\"\n\"Root/Mail\",\"Mail\",\"a\",\"p,\"\"q\",\"https://x\",\"n\",\"\"\n";

        Run("KeePass XML", Encoding.UTF8.GetBytes(Xml), d => KeePassImporter.ParseXml(Text(d)), Iterations);
        Run("KeePass CSV", Encoding.UTF8.GetBytes(Csv), d => KeePassImporter.ParseCsv(Text(d)), Iterations);
    }

    [Fact]
    public void TheVaultContents_AreRefusedWhenDamaged()
    {
        var fileId = Guid.NewGuid();
        var sample = VaultContentsCodec.Encode(new VaultContents
        {
            AttachmentData = new Dictionary<Guid, byte[]> { [fileId] = [1, 2, 3] },
            Accounts = [new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }],
            Passwords =
            [
                new PasswordEntry
                {
                    Title = "Mail",
                    Password = "p@ss",
                    Attachments = [new PasswordAttachment { Id = fileId, Name = "a.txt", Size = 3 }],
                },
            ],
        });

        Run("VaultContentsCodec", sample, d => VaultContentsCodec.Decode(d), Iterations);
    }

    [Fact]
    public async Task ADamagedVaultFile_NeverOpensWithChangedContents()
    {
        var path = Path.Combine(Path.GetTempPath(), "ms-fuzz-" + Guid.NewGuid().ToString("N") + ".sauth");
        var kdf = new Argon2Parameters { MemorySizeKib = 1024, Iterations = 1, DegreeOfParallelism = 1, KeyLength = 32 };
        VaultService Open() => new(new FileVaultRepository(path), new AesGcmEncryptionService(), new MasterPasswordService(), kdf);

        try
        {
            await Open().SaveContentsAsync("pw", new VaultContents
            {
                Accounts = [new OtpAccount { Issuer = "GitHub", Label = "a@b.c", Secret = "JBSWY3DPEHPK3PXP" }],
            });
            var sample = File.ReadAllBytes(path);

            // Each round does a real key derivation, so fewer of them.
            Run("Vault file", sample, d =>
            {
                File.WriteAllBytes(path, d);
                var contents = Open().OpenContentsAsync("pw").GetAwaiter().GetResult();
                contents.Accounts.Should().ContainSingle().Which.Secret.Should().Be("JBSWY3DPEHPK3PXP", "AES-GCM detects any change to the data");
            }, Iterations / 20);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
