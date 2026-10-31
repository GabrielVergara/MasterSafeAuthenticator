// File:    DataTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MasterSafe.Core;
using MasterSafe.Data;
using Xunit;

namespace MasterSafe.Tests;

public class DataTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<VaultDbContext> _options;

    public DataTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<VaultDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new VaultDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    [Fact]
    public async Task AddAndGet_PersistsEncryptedSecretOnly()
    {
        await using var ctx = new VaultDbContext(_options);
        var repo = new AccountRepository(ctx);

        var account = new OtpAccount { Issuer = "GitHub", Label = "alice", Secret = "JBSWY3DPEHPK3PXP" };
        var encrypted = new byte[] { 1, 2, 3, 4, 5 };

        var id = await repo.AddAsync(account, encrypted);
        id.Should().Be(account.Id);

        var all = await repo.GetAllAsync();
        all.Should().HaveCount(1);
        all[0].EncryptedSecret.Should().Equal(encrypted);
        all[0].Issuer.Should().Be("GitHub");
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        await using var ctx = new VaultDbContext(_options);
        var repo = new AccountRepository(ctx);
        var account = new OtpAccount { Secret = "JBSWY3DPEHPK3PXP" };
        await repo.AddAsync(account, [9, 9]);

        (await repo.DeleteAsync(account.Id)).Should().BeTrue();
        (await repo.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public void Schema_ContainsExpectedTables()
    {
        using var ctx = new VaultDbContext(_options);
        var tables = ctx.Model.GetEntityTypes().Select(t => t.GetTableName()).ToList();
        tables.Should().Contain(["Accounts", "Tags", "VaultMetadata"]);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
