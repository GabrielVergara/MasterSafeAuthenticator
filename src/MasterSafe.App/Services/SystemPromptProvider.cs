// File:    SystemPromptProvider.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using MasterSafe.Crypto;

namespace MasterSafe.App.Services;

/// <summary>Lifts the process memory block only while Windows shows its own prompt for Windows Hello.</summary>
public sealed class SystemPromptProvider(ISecretProvider inner) : ISecretProvider
{
    /// <inheritdoc />
    public string SlotType => inner.SlotType;

    /// <inheritdoc />
    public string DisplayName => inner.DisplayName;

    /// <inheritdoc />
    public string EnrollmentHint => inner.EnrollmentHint;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => inner.IsAvailableAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<SecretEnrollment> EnrollAsync(string slotId, CancellationToken cancellationToken = default)
    {
        using (ProcessHardening.OpenForSystemPrompt())
        {
            return await inner.EnrollAsync(slotId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> DeriveAsync(string slotId, byte[] enrollmentData, CancellationToken cancellationToken = default)
    {
        using (ProcessHardening.OpenForSystemPrompt())
        {
            return await inner.DeriveAsync(slotId, enrollmentData, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string slotId, CancellationToken cancellationToken = default) => inner.RemoveAsync(slotId, cancellationToken);
}
