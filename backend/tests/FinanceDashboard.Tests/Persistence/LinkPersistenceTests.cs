using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class LinkPersistenceTests(PostgresFixture postgres)
{
    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var m = PersistenceTestSupport.Create(cs, null);
        await m.Database.MigrateAsync();
        return cs;
    }

    private static Transaction Tx(Guid accountId, decimal amount) => new()
    {
        Id = Guid.NewGuid(), AccountId = accountId, Date = new DateOnly(2026, 10, 1), Amount = amount,
        OriginalDescription = "x", NormalizedDescription = "x",
    };

    private static Link NewLink(Transaction o, Transaction i, LinkState state = LinkState.Confirmed) => new()
    {
        Id = Guid.NewGuid(), Kind = LinkKind.OwnTransfer, State = state, OutTransactionId = o.Id,
        InTransactionId = i.Id, Origin = LinkOrigin.User, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<(Account, List<Transaction>)> SeedAsync(AppDbContext db, int count)
    {
        var a = new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.Checking };
        db.Accounts.Add(a);
        var txs = Enumerable.Range(0, count).Select(n => Tx(a.Id, n % 2 == 0 ? -10m : 10m)).ToList();
        db.Transactions.AddRange(txs);
        await db.SaveChangesAsync();
        return (a, txs);
    }

    private static async Task AssertUniqueViolationAsync(AppDbContext db)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23505", pg.SqlState);
    }

    [Fact]
    public async Task Confirmed_unique_per_side()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var (_, t) = await SeedAsync(db, 4);
        db.Links.Add(NewLink(t[0], t[1]));
        await db.SaveChangesAsync();

        db.Links.Add(NewLink(t[0], t[3])); // same out
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();

        db.Links.Add(NewLink(t[2], t[1])); // same in
        await AssertUniqueViolationAsync(db);
        db.ChangeTracker.Clear();

        db.Links.Add(NewLink(t[0], t[3], LinkState.Suggested));
        db.Links.Add(NewLink(t[0], t[2], LinkState.Suggested));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Links.CountAsync());
        Assert.Equal(1, await db.Links.CountAsync(l => l.State == LinkState.Confirmed));
    }

    [Fact]
    public async Task Rejected_or_suggested_does_not_block_confirmed_on_same_line()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var (_, t) = await SeedAsync(db, 4);
        db.Links.Add(NewLink(t[0], t[1], LinkState.Rejected));
        db.Links.Add(NewLink(t[0], t[3], LinkState.Suggested));
        await db.SaveChangesAsync();

        db.Links.Add(NewLink(t[0], t[2], LinkState.Confirmed));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Links.CountAsync());
    }

    [Fact]
    public async Task Cross_column_confirmed_is_not_blocked_by_indexes_service_must_guard()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var (_, t) = await SeedAsync(db, 3);
        db.Links.Add(NewLink(t[0], t[1]));
        await db.SaveChangesAsync();

        // t[1] is In of the first Confirmed link; here it is Out. Indexes are per column, so this saves:
        // LinkService must reject a Confirm when the line is already in any Confirmed link on either side.
        db.Links.Add(NewLink(t[1], t[2]));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Links.CountAsync(l => l.State == LinkState.Confirmed));
    }

    [Fact]
    public async Task Duplicate_pair_is_rejected()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var (_, t) = await SeedAsync(db, 2);
        db.Links.Add(NewLink(t[0], t[1], LinkState.Suggested));
        await db.SaveChangesAsync();

        db.Links.Add(NewLink(t[0], t[1], LinkState.Rejected));
        await AssertUniqueViolationAsync(db);
    }

    [Fact]
    public async Task Deleting_transaction_cascades_links()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var (_, t) = await SeedAsync(db, 2);
        db.Links.Add(NewLink(t[0], t[1]));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await db.Transactions.Where(x => x.Id == t[0].Id).ExecuteDeleteAsync();
        Assert.Empty(await db.Links.ToListAsync());
    }

    [Fact]
    public async Task Other_user_cannot_see_links()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a"))
        {
            var (_, t) = await SeedAsync(a, 2);
            a.Links.Add(NewLink(t[0], t[1]));
            await a.SaveChangesAsync();
        }
        await using var b = PersistenceTestSupport.Create(cs, "user-b");
        Assert.Empty(await b.Links.ToListAsync());
        await using var a2 = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Single(await a2.Links.ToListAsync());
    }

    [Fact]
    public async Task WithoutConfirmedLink_excludes_both_sides_only_when_confirmed()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var (_, t) = await SeedAsync(db, 6);
        db.Links.Add(NewLink(t[0], t[1], LinkState.Confirmed));
        db.Links.Add(NewLink(t[2], t[3], LinkState.Suggested));
        db.Links.Add(NewLink(t[4], t[5], LinkState.Rejected));
        await db.SaveChangesAsync();

        var ids = (await db.Transactions.WithoutConfirmedLink(db).Select(x => x.Id).ToListAsync()).ToHashSet();
        Assert.DoesNotContain(t[0].Id, ids);
        Assert.DoesNotContain(t[1].Id, ids);
        Assert.Equal(4, ids.Count);
        foreach (var x in t.Skip(2)) Assert.Contains(x.Id, ids);
    }
}
