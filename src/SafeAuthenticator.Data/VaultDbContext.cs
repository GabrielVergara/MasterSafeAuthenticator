// File:    VaultDbContext.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using Microsoft.EntityFrameworkCore;
using SafeAuthenticator.Data.Entities;

namespace SafeAuthenticator.Data;

/// <summary>
/// EF Core context for the SQLite-backed account store. Secrets are stored encrypted only.
/// </summary>
public sealed class VaultDbContext : DbContext
{
    public VaultDbContext(DbContextOptions<VaultDbContext> options)
        : base(options)
    {
    }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<TagEntity> Tags => Set<TagEntity>();

    public DbSet<VaultMetadataEntity> VaultMetadata => Set<VaultMetadataEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AccountEntity>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasMany(a => a.Tags)
                .WithMany(t => t.Accounts)
                .UsingEntity(j => j.ToTable("AccountTags"));
        });

        modelBuilder.Entity<TagEntity>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<VaultMetadataEntity>(e => e.HasKey(m => m.Id));
    }
}
