using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class ConstraintTests(PostgresFixture postgres)
{
    private async Task<AppDbContext> NewDbAsync(string user = "user-a")
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var m = PersistenceTestSupport.Create(cs, null)) await m.Database.MigrateAsync();
        return PersistenceTestSupport.Create(cs, user);
    }

    private static Account NewAccount() => new() { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.Checking };

    private static Transaction Tx(Guid accountId, string? key, int ordinal, decimal amount = -1m) => new()
    {
        Id = Guid.NewGuid(), AccountId = accountId, Date = new DateOnly(2026, 10, 1), Amount = amount,
        OriginalDescription = "x", NormalizedDescription = "x", SourceKey = key, Ordinal = ordinal,
    };

    private static ImportBatch Batch(Guid accountId, string hash, ImportBatchState state) => new()
    {
        Id = Guid.NewGuid(), AccountId = accountId, FileHash = hash, Source = ImportSource.NubankChecking,
        State = state, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Duplicate_source_key_and_ordinal_in_same_account_is_rejected()
    {
        await using var db = await NewDbAsync();
        var a = NewAccount();
        db.Accounts.Add(a);
        db.Transactions.Add(Tx(a.Id, "k1", 0));
        await db.SaveChangesAsync();

        db.Transactions.Add(Tx(a.Id, "k1", 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Null_source_key_is_not_constrained()
    {
        await using var db = await NewDbAsync();
        var a = NewAccount();
        db.Accounts.Add(a);
        db.Transactions.AddRange(Tx(a.Id, null, 0), Tx(a.Id, null, 0));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Same_source_key_in_different_accounts_is_allowed()
    {
        await using var db = await NewDbAsync();
        var a = NewAccount();
        var b = NewAccount();
        db.Accounts.AddRange(a, b);
        db.Transactions.AddRange(Tx(a.Id, "k1", 0), Tx(b.Id, "k1", 0));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Confirmed_hash_is_unique_per_user_but_reusable_after_undone()
    {
        await using var db = await NewDbAsync();
        var a = NewAccount();
        db.Accounts.Add(a);
        var first = Batch(a.Id, "hash", ImportBatchState.Confirmed);
        db.ImportBatches.Add(first);
        db.ImportBatches.Add(Batch(a.Id, "hash", ImportBatchState.Preview)); // previews may repeat
        await db.SaveChangesAsync();

        db.ImportBatches.Add(Batch(a.Id, "hash", ImportBatchState.Confirmed));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var tracked = await db.ImportBatches.SingleAsync(x => x.Id == first.Id);
        tracked.State = ImportBatchState.Undone;
        await db.SaveChangesAsync();

        db.ImportBatches.Add(Batch(a.Id, "hash", ImportBatchState.Confirmed));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Confirmed_hash_can_be_used_by_another_user()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using (var m = PersistenceTestSupport.Create(cs, null)) await m.Database.MigrateAsync();
        foreach (var user in new[] { "user-a", "user-b" })
        {
            await using var db = PersistenceTestSupport.Create(cs, user);
            var acc = NewAccount();
            db.Accounts.Add(acc);
            db.ImportBatches.Add(Batch(acc.Id, "same", ImportBatchState.Confirmed));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Deleting_category_nulls_transaction_category()
    {
        await using var db = await NewDbAsync();
        var a = NewAccount();
        var c = new Category { Id = Guid.NewGuid(), Name = "C", Kind = CategoryKind.Expense };
        db.Accounts.Add(a);
        db.Categories.Add(c);
        var t = Tx(a.Id, "k", 0);
        t.CategoryId = c.Id;
        db.Transactions.Add(t);
        await db.SaveChangesAsync();

        db.Categories.Remove(c);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Null((await db.Transactions.SingleAsync()).CategoryId);
    }

    [Fact]
    public async Task Amount_roundtrips_with_two_decimals()
    {
        await using var db = await NewDbAsync();
        var a = NewAccount();
        db.Accounts.Add(a);
        db.Transactions.Add(Tx(a.Id, "k", 0, -1234.56m));
        db.Checkpoints.Add(new BalanceCheckpoint { Id = Guid.NewGuid(), AccountId = a.Id, Date = new DateOnly(2026, 10, 1), Amount = 99999999999999.99m, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(-1234.56m, (await db.Transactions.SingleAsync()).Amount);
        Assert.Equal(99999999999999.99m, (await db.Checkpoints.SingleAsync()).Amount);
    }

    [Fact]
    public async Task Enums_are_stored_as_strings()
    {
        await using var db = await NewDbAsync();
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.CreditCard });
        await db.SaveChangesAsync();
        var raw = await db.Database.SqlQueryRaw<string>("SELECT \"Type\" AS \"Value\" FROM \"Accounts\"").SingleAsync();
        Assert.Equal("CreditCard", raw);
    }
}
