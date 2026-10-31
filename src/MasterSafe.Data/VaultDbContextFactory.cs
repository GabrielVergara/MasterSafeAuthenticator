// File:    VaultDbContextFactory.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Microsoft.EntityFrameworkCore;

namespace MasterSafe.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can create migrations without a running host.
/// </summary>
public sealed class VaultDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<VaultDbContext>
{
    public VaultDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VaultDbContext>()
            .UseSqlite("Data Source=mastersafe.db")
            .Options;
        return new VaultDbContext(options);
    }
}
