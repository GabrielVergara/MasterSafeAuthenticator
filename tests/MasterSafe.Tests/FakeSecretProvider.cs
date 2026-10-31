// File:    FakeSecretProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Security.Cryptography;
using System.Text;
using MasterSafe.Crypto;

namespace MasterSafe.Tests;

/// <summary>An in-memory stand-in for Windows Hello or a security key.</summary>
internal sealed class FakeSecretProvider : ISecretProvider
{
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);

    public bool Available { get; set; } = true;

    public bool Cancel { get; set; }

    public List<string> Removed { get; } = [];

    public string SlotType => SlotTypes.WindowsHello;

    public string DisplayName => "Fake Hello";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);

    public Task<SecretEnrollment> EnrollAsync(string slotId, CancellationToken cancellationToken = default)
        => Task.FromResult(new SecretEnrollment(Encoding.UTF8.GetBytes("challenge:" + slotId), Derive(slotId)));

    public Task<byte[]> DeriveAsync(string slotId, byte[] enrollmentData, CancellationToken cancellationToken = default)
    {
        if (Cancel)
        {
            throw new OperationCanceledException();
        }

        return Task.FromResult(Derive(slotId));
    }

    public Task RemoveAsync(string slotId, CancellationToken cancellationToken = default)
    {
        Removed.Add(slotId);
        return Task.CompletedTask;
    }

    private byte[] Derive(string slotId)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, _secret, 32, Encoding.UTF8.GetBytes(slotId), Encoding.UTF8.GetBytes("fake"));
}
