using Microsoft.EntityFrameworkCore;

namespace SafeAuthenticator.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can create migrations without a running host.
/// </summary>
public sealed class VaultDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<VaultDbContext>
{
    public VaultDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VaultDbContext>()
            .UseSqlite("Data Source=safeauthenticator.db")
            .Options;
        return new VaultDbContext(options);
    }
}
