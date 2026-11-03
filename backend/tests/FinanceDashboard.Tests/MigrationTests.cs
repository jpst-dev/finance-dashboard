using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceDashboard.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_create_identity_tables_on_an_empty_database()
    {
        await using var factory = new ApiFactory(
            await postgres.CreateDatabaseAsync(),
            new() { ["Database:MigrateOnStartup"] = "true" });
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Migration_applies_over_a_database_with_S3a_data()
    {
        var cs = await postgres.CreateDatabaseAsync();
        var linkId = Guid.NewGuid();
        Guid outId, inId;
        await using (var db = PersistenceTestSupport.Create(cs, "user-a"))
        {
            await db.GetService<IMigrator>().MigrateAsync("S3aLinks");
            var acc = new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.Checking };
            Transaction Tx(decimal amount) => new()
            {
                Id = Guid.NewGuid(), AccountId = acc.Id, Date = new DateOnly(2026, 10, 1), Amount = amount,
                OriginalDescription = "x", NormalizedDescription = "x",
            };
            var o = Tx(-10m);
            var i = Tx(10m);
            outId = o.Id;
            inId = i.Id;
            db.Accounts.Add(acc);
            db.Transactions.AddRange(o, i);
            db.Links.Add(new Link
            {
                Id = linkId, Kind = LinkKind.OwnTransfer, State = LinkState.Confirmed, OutTransactionId = o.Id,
                InTransactionId = i.Id, Origin = LinkOrigin.User, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var after = PersistenceTestSupport.Create(cs, "user-a");
        await after.Database.MigrateAsync();
        var link = await after.Links.SingleAsync(l => l.Id == linkId);
        Assert.Equal(outId, link.OutTransactionId);
        Assert.Equal(inId, link.InTransactionId);
        Assert.Equal(0, await after.OwnAliases.CountAsync());
    }
}
