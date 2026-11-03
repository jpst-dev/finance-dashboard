using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class CategoryRulePersistenceTests(PostgresFixture postgres)
{
    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var m = PersistenceTestSupport.Create(cs, null);
        await m.Database.MigrateAsync();
        return cs;
    }

    private static async Task<Category> CategoryAsync(AppDbContext db, string name = "Mercado")
    {
        var category = new Category { Id = Guid.NewGuid(), Name = name, Kind = CategoryKind.Expense };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static CategoryRule Rule(string pattern, Guid categoryId) => new()
    {
        Id = Guid.NewGuid(), Pattern = pattern, CategoryId = categoryId, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Pattern_unique_per_user()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var category = await CategoryAsync(db);
        db.CategoryRules.Add(Rule("SUPERMERCADO EXEMPLO", category.Id));
        await db.SaveChangesAsync();

        db.CategoryRules.Add(Rule("SUPERMERCADO EXEMPLO", category.Id));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task Rule_cascades_when_category_deleted()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var category = await CategoryAsync(db);
        var other = await CategoryAsync(db, "Outros");
        db.CategoryRules.AddRange(Rule("SUPERMERCADO EXEMPLO", category.Id), Rule("PADARIA FICTICIA", other.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await db.Categories.Where(c => c.Id == category.Id).ExecuteDeleteAsync();

        var left = Assert.Single(await db.CategoryRules.AsNoTracking().ToListAsync());
        Assert.Equal("PADARIA FICTICIA", left.Pattern);
    }

    [Fact]
    public async Task Rules_isolated_between_users()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a"))
        {
            a.CategoryRules.Add(Rule("SUPERMERCADO EXEMPLO", (await CategoryAsync(a)).Id));
            await a.SaveChangesAsync();
        }

        await using (var b = PersistenceTestSupport.Create(cs, "user-b"))
        {
            Assert.Empty(await b.CategoryRules.ToListAsync());
            b.CategoryRules.Add(Rule("SUPERMERCADO EXEMPLO", (await CategoryAsync(b)).Id)); // same pattern, other user
            await b.SaveChangesAsync();
            Assert.Single(await b.CategoryRules.ToListAsync());
        }

        await using var a2 = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Equal("user-a", Assert.Single(await a2.CategoryRules.ToListAsync()).UserId);
    }

    [Fact]
    public async Task Migration_applies_over_a_database_with_S3b_data()
    {
        var cs = await postgres.CreateDatabaseAsync();
        Guid categoryId, lineId;
        await using (var db = PersistenceTestSupport.Create(cs, "user-a"))
        {
            await db.GetService<IMigrator>().MigrateAsync("S3bOpenLinks");
            var acc = new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.Checking };
            var category = new Category { Id = Guid.NewGuid(), Name = "Mercado", Kind = CategoryKind.Expense };
            var line = new Transaction
            {
                Id = Guid.NewGuid(), AccountId = acc.Id, Date = new DateOnly(2026, 10, 1), Amount = -10m,
                OriginalDescription = "SUPERMERCADO EXEMPLO", NormalizedDescription = "SUPERMERCADO EXEMPLO",
                CategoryId = category.Id,
            };
            categoryId = category.Id;
            lineId = line.Id;
            db.Accounts.Add(acc);
            db.Categories.Add(category);
            db.Transactions.Add(line);
            db.OwnAliases.Add(new OwnAlias { Id = Guid.NewGuid(), Fragment = "JOAO TESTE", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await using var after = PersistenceTestSupport.Create(cs, "user-a");
        await after.Database.MigrateAsync();
        Assert.Equal(categoryId, (await after.Transactions.SingleAsync(t => t.Id == lineId)).CategoryId);
        Assert.Equal(1, await after.OwnAliases.CountAsync());
        Assert.Equal(0, await after.CategoryRules.CountAsync());
    }
}
