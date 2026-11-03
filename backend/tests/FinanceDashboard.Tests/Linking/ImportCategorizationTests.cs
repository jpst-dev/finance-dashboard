using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Linking;

/// <summary>Category rules applied at import confirm, through HTTP on Postgres (synthetic files only).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportCategorizationTests(PostgresFixture postgres)
{
    private const string Market = "Compra no debito - SUPERMERCADO EXEMPLO";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static string Ofx(int day) => $"202610{day:00}000000[-3:BRT]";

    private static async Task<Guid> CategoryAsync(AuthedClient c, string name, string kind = "Expense")
    {
        var r = await c.PostAsync("/api/categories", new { name, kind });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    private static async Task RuleAsync(ApiFactory f, AuthedClient c, string pattern, Guid categoryId)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        db.CategoryRules.Add(new CategoryRule
        {
            Id = Guid.NewGuid(), Pattern = pattern, CategoryId = categoryId, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<Transaction>> LinesAsync(ApiFactory f, AuthedClient c)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        return await db.Transactions.AsNoTracking().OrderBy(t => t.Date).ToListAsync();
    }

    [Fact]
    public async Task Import_confirm_categorizes_new_lines_and_reports_count()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var account = await CreateAccountAsync(c);
        var mercado = await CategoryAsync(c, "Mercado");

        // A line from an earlier batch, imported before the rule existed: only "Aplicar regras" reaches it.
        var earlier = await PreviewAsync(c, account, OfxTestFile.Checking().Line(Ofx(1), "-9.00", Market, Fit3).Build());
        Assert.Equal(0, (await ConfirmOkAsync(c, earlier)).GetProperty("categorized").GetInt32());

        await RuleAsync(f, c, "SUPERMERCADO EXEMPLO", mercado);
        var file = OfxTestFile.Checking()
            .Line(Ofx(5), "-50.00", Market, Fit1)
            .Line(Ofx(6), "-7.00", "Compra no debito - PADARIA FICTICIA", Fit2)
            .Build();
        var batch = await PreviewAsync(c, account, file);
        var result = await ConfirmOkAsync(c, batch);

        Assert.Equal(1, result.GetProperty("categorized").GetInt32());
        var lines = await LinesAsync(f, c);
        var byKey = lines.ToDictionary(t => t.SourceKey!);
        Assert.Equal(mercado, byKey[Fit1].CategoryId);
        Assert.False(byKey[Fit1].EditedByUser);
        Assert.Null(byKey[Fit2].CategoryId);
        Assert.Null(byKey[Fit3].CategoryId);
    }

    [Fact]
    public async Task Import_preview_override_category_wins_over_rule()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var account = await CreateAccountAsync(c);
        var mercado = await CategoryAsync(c, "Mercado");
        var lazer = await CategoryAsync(c, "Lazer");
        await RuleAsync(f, c, "SUPERMERCADO EXEMPLO", mercado);
        var batch = await PreviewAsync(c, account, OfxTestFile.Checking().Line(Ofx(5), "-50.00", Market, Fit1).Build());

        var r = await ConfirmWithAsync(c, batch, false, new { sourceKey = Fit1, categoryId = lazer });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, (await JsonOf(r)).GetProperty("categorized").GetInt32());
        Assert.Equal(lazer, Assert.Single(await LinesAsync(f, c)).CategoryId);
    }

    [Fact]
    public async Task Import_without_rules_reports_zero()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var account = await CreateAccountAsync(c);
        var batch = await PreviewAsync(c, account, ThreeLines().Build());

        var result = await ConfirmOkAsync(c, batch);

        Assert.Equal(3, result.GetProperty("created").GetInt32());
        Assert.Equal(0, result.GetProperty("categorized").GetInt32());
        Assert.All(await LinesAsync(f, c), t => Assert.Null(t.CategoryId));
    }
}
