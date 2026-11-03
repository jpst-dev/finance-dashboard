using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class OwnAliasPersistenceTests(PostgresFixture postgres)
{
    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var m = PersistenceTestSupport.Create(cs, null);
        await m.Database.MigrateAsync();
        return cs;
    }

    private static OwnAlias Alias(string fragment) => new()
    {
        Id = Guid.NewGuid(), Fragment = fragment, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task OwnAlias_unique_per_user_and_fragment()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        db.OwnAliases.Add(Alias("JOAO TESTE"));
        await db.SaveChangesAsync();

        db.OwnAliases.Add(Alias("JOAO TESTE"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task OwnAlias_isolated_between_users()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a"))
        {
            a.OwnAliases.Add(Alias("JOAO TESTE"));
            await a.SaveChangesAsync();
        }
        await using (var b = PersistenceTestSupport.Create(cs, "user-b"))
        {
            Assert.Empty(await b.OwnAliases.ToListAsync());
            b.OwnAliases.Add(Alias("JOAO TESTE")); // same fragment, other user: allowed
            await b.SaveChangesAsync();
            Assert.Single(await b.OwnAliases.ToListAsync());
        }
        await using var a2 = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Equal("user-a", Assert.Single(await a2.OwnAliases.ToListAsync()).UserId);
    }
}
