using Microsoft.EntityFrameworkCore;
using SafeAuthenticator.Core;
using SafeAuthenticator.Data.Entities;

namespace SafeAuthenticator.Data;

/// <summary>
/// Repository for account rows. Callers supply already-encrypted secret bytes;
/// this layer never performs encryption itself and never stores plaintext secrets.
/// </summary>
public sealed class AccountRepository
{
    private readonly VaultDbContext _db;

    public AccountRepository(VaultDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>Ensures the database/schema exists.</summary>
    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        => _db.Database.EnsureCreatedAsync(cancellationToken);

    /// <summary>Adds an account with its pre-encrypted secret.</summary>
    public async Task<Guid> AddAsync(OtpAccount account, byte[] encryptedSecret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(encryptedSecret);

        var entity = new AccountEntity
        {
            Id = account.Id,
            Issuer = account.Issuer,
            Label = account.Label,
            EncryptedSecret = encryptedSecret,
            Type = account.Type.ToString(),
            Algorithm = account.Algorithm.ToString(),
            Digits = account.Digits,
            Period = account.Period,
            Counter = account.Counter,
        };

        _db.Accounts.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
    }

    /// <summary>Returns all account rows (secrets remain encrypted).</summary>
    public async Task<IReadOnlyList<AccountEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _db.Accounts.Include(a => a.Tags).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes an account by id.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Accounts.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return false;
        }

        _db.Accounts.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
