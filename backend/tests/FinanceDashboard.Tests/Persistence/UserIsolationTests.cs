using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class UserIsolationTests(PostgresFixture postgres)
{
    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PersistenceTestSupport.Create(cs, null);
        await db.Database.MigrateAsync();
        return cs;
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var account = new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.Checking };
        var category = new Category { Id = Guid.NewGuid(), Name = "C", Kind = CategoryKind.Expense };
        var batch = new ImportBatch { Id = Guid.NewGuid(), AccountId = account.Id, FileHash = "h", Source = ImportSource.NubankChecking, State = ImportBatchState.Preview, CreatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(account);
        db.Categories.Add(category);
        db.ImportBatches.Add(batch);
        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, Date = new DateOnly(2026, 10, 1), Amount = -1m, OriginalDescription = "x", NormalizedDescription = "x" });
        db.Checkpoints.Add(new BalanceCheckpoint { Id = Guid.NewGuid(), AccountId = account.Id, Date = new DateOnly(2026, 10, 1), Amount = 1m, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Query_filter_hides_other_users_rows_for_every_entity()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a")) await SeedAsync(a);

        await using var b = PersistenceTestSupport.Create(cs, "user-b");
        Assert.Empty(await b.Accounts.ToListAsync());
        Assert.Empty(await b.Categories.ToListAsync());
        Assert.Empty(await b.Transactions.ToListAsync());
        Assert.Empty(await b.Checkpoints.ToListAsync());
        Assert.Empty(await b.ImportBatches.ToListAsync());

        await using var a2 = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Single(await a2.Accounts.ToListAsync());
        Assert.Single(await a2.Categories.ToListAsync());
        Assert.Single(await a2.Transactions.ToListAsync());
        Assert.Single(await a2.Checkpoints.ToListAsync());
        Assert.Single(await a2.ImportBatches.ToListAsync());
        Assert.All(await a2.Accounts.ToListAsync(), x => Assert.Equal("user-a", x.UserId));
    }

    [Fact]
    public async Task Saving_without_current_user_throws()
    {
        var cs = await NewDbAsync();
        await using var db = PersistenceTestSupport.Create(cs, null);
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task IgnoreQueryFilters_sees_all()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a")) await SeedAsync(a);
        await using (var b = PersistenceTestSupport.Create(cs, "user-b")) await SeedAsync(b);

        await using var anon = PersistenceTestSupport.Create(cs, null);
        Assert.Empty(await anon.Accounts.ToListAsync());
        Assert.Equal(2, await anon.Accounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await anon.Transactions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Visible_excludes_soft_deleted_transactions()
    {
        var cs = await NewDbAsync();
        await using var db = PersistenceTestSupport.Create(cs, "user-a");
        await SeedAsync(db);
        var t = await db.Transactions.SingleAsync();
        t.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.Empty(await db.Transactions.Visible().ToListAsync());
        Assert.Single(await db.Transactions.ToListAsync());
    }
}
