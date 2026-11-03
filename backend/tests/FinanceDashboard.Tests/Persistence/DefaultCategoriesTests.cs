using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class DefaultCategoriesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ensure_is_idempotent_and_covers_existing_users()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient(); // starts host, migrates

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var email in new[] { "one@example.com", "two@example.com" })
                Assert.True((await users.CreateAsync(new AppUser { UserName = email, Email = email }, "Pass-Word-42!")).Succeeded);
        }

        await DefaultCategories.EnsureForAllUsersAsync(factory.Services);
        await DefaultCategories.EnsureForAllUsersAsync(factory.Services);

        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var all = await db.Categories.IgnoreQueryFilters().ToListAsync();
        var userIds = await db.Users.Select(u => u.Id).ToListAsync();

        Assert.True(userIds.Count >= 2);
        foreach (var id in userIds)
        {
            var mine = all.Where(c => c.UserId == id).ToList();
            Assert.Equal(13, mine.Count);
            Assert.Equal(10, mine.Count(c => c.Kind == CategoryKind.Expense));
            Assert.Equal(3, mine.Count(c => c.Kind == CategoryKind.Income));
            Assert.All(mine, c => Assert.True(c.IsDefault));
            Assert.Contains(mine, c => c.Name == "Impostos e taxas");
        }
    }

    [Fact]
    public async Task Ensure_does_not_duplicate_existing_names()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), UserId = "u1", Name = "Alimentação", Kind = CategoryKind.Expense });
        await db.SaveChangesAsync();

        await DefaultCategories.EnsureForUserAsync(db, "u1");
        var mine = await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == "u1").ToListAsync();
        Assert.Equal(13, mine.Count);
        Assert.Single(mine, c => c.Name == "Alimentação");
    }
}
