using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Tests.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.CategoryRules;

/// <summary>Category rules API through HTTP on Postgres (synthetic files only).</summary>
[Collection(PostgresCollection.Name)]
public sealed class CategoryRuleApiTests(PostgresFixture postgres)
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

    private static Task<HttpResponseMessage> CreateRuleAsync(AuthedClient c, string pattern, Guid categoryId) =>
        c.PostAsync("/api/category-rules", new { pattern, categoryId });

    private static async Task<JsonElement> ListAsync(AuthedClient c)
    {
        var r = await c.GetAsync("/api/category-rules");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    private static async Task ImportAsync(AuthedClient c, Guid account, byte[] file)
    {
        var batch = await PreviewAsync(c, account, file);
        await ConfirmOkAsync(c, batch);
    }

    [Fact]
    public async Task Crud_round_trip()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");

        var created = await CreateRuleAsync(c, "  supermercado   exemplo ", mercado);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonOf(created);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("SUPERMERCADO EXEMPLO", body.GetProperty("pattern").GetString());
        Assert.Equal(mercado, body.GetProperty("categoryId").GetGuid());
        Assert.Equal("Mercado", body.GetProperty("categoryName").GetString());
        Assert.Equal(0, body.GetProperty("matches").GetInt32());
        Assert.NotEqual(default, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.EndsWith(id.ToString(), created.Headers.Location!.ToString());

        var list = await ListAsync(c);
        var only = Assert.Single(list.EnumerateArray());
        Assert.Equal(id, only.GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/category-rules/{id}")).StatusCode);
        Assert.Equal(0, (await ListAsync(c)).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/category-rules/{id}")).StatusCode);
    }

    [Fact]
    public async Task Other_user_gets_404_on_delete()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var mercado = await CategoryAsync(a, "Mercado");
        var id = (await JsonOf(await CreateRuleAsync(a, "SUPERMERCADO", mercado))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/category-rules/{id}")).StatusCode);
        Assert.Equal(0, (await ListAsync(b)).GetArrayLength());
        Assert.Equal(1, (await ListAsync(a)).GetArrayLength());
        // Another user's category is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await CreateRuleAsync(b, "SUPERMERCADO", mercado)).StatusCode);
    }

    [Fact]
    public async Task Requests_without_antiforgery_are_rejected()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");
        var id = (await JsonOf(await CreateRuleAsync(c, "SUPERMERCADO", mercado))).GetProperty("id").GetGuid();

        var body = new { pattern = "PADARIA", categoryId = mercado };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.Raw.PostAsync("/api/category-rules", JsonContent.Create(body))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.PostAsync("/api/category-rules/apply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.DeleteAsync($"/api/category-rules/{id}")).StatusCode);
        Assert.Equal(1, (await ListAsync(c)).GetArrayLength());
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        await using var f = await NewFactoryAsync();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/category-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/category-rules", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/category-rules/apply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"/api/category-rules/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("   ab   ")]
    [InlineData("      ")]
    public async Task Create_invalid_pattern_400_short_or_blank(string pattern)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");

        var r = await CreateRuleAsync(c, pattern, mercado);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("invalid_pattern", await ErrorOf(r));
        Assert.Equal(0, (await ListAsync(c)).GetArrayLength());
    }

    [Fact]
    public async Task Create_missing_or_null_pattern_400()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");

        foreach (var body in new object[] { new { categoryId = mercado }, new { pattern = (string?)null, categoryId = mercado } })
        {
            var r = await c.PostAsync("/api/category-rules", body);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("invalid_pattern", await ErrorOf(r));
        }

        Assert.Equal(0, (await ListAsync(c)).GetArrayLength());
    }

    [Fact]
    public async Task Create_invalid_pattern_400_too_long()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");

        var r101 = await CreateRuleAsync(c, new string('A', 101), mercado);
        Assert.Equal(HttpStatusCode.BadRequest, r101.StatusCode);
        Assert.Equal("invalid_pattern", await ErrorOf(r101));
        // Raw input over 200 is refused even if it would normalize short.
        var raw = "ABC" + new string(' ', 250);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRuleAsync(c, raw, mercado)).StatusCode);
        // 100 is the largest accepted.
        Assert.Equal(HttpStatusCode.Created, (await CreateRuleAsync(c, new string('A', 100), mercado)).StatusCode);
    }

    [Fact]
    public async Task Create_unknown_category_404()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateRuleAsync(c, "SUPERMERCADO", Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Create_duplicate_409_duplicate_rule()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");
        var casa = await CategoryAsync(c, "Casa");
        Assert.Equal(HttpStatusCode.Created, (await CreateRuleAsync(c, "Supermercado Exemplo", mercado)).StatusCode);

        var r = await CreateRuleAsync(c, "  SUPERMERCADO    EXEMPLO ", casa);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("duplicate_rule", await ErrorOf(r));
    }

    [Fact]
    public async Task Concurrent_duplicate_creation_returns_409_not_500()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mercado = await CategoryAsync(c, "Mercado");

        var calls = Enumerable.Range(0, 8)
            .Select(_ => ("/api/category-rules", (object)new { pattern = "SUPERMERCADO EXEMPLO", categoryId = mercado }))
            .ToList();
        var results = await RaceAsync(c, calls);

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await ListAsync(c)).GetArrayLength());
    }

    [Fact]
    public async Task List_matches_reflect_candidates()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var account = await CreateAccountAsync(c);
        var mercado = await CategoryAsync(c, "Mercado");
        await ImportAsync(c, account, OfxTestFile.Checking()
            .Line(Ofx(5), "-50.00", Market, Fit1)
            .Line(Ofx(6), "-20.00", Market + " 2", Fit2)
            .Line(Ofx(7), "-7.00", "Compra no debito - PADARIA FICTICIA", Fit3).Build());

        var created = await JsonOf(await CreateRuleAsync(c, "supermercado exemplo", mercado));
        Assert.Equal(2, created.GetProperty("matches").GetInt32());
        var item = Assert.Single((await ListAsync(c)).EnumerateArray());
        Assert.Equal(2, item.GetProperty("matches").GetInt32());

        // After applying, nothing is left to catch.
        Assert.Equal(2, (await JsonOf(await c.PostAsync("/api/category-rules/apply"))).GetProperty("applied").GetInt32());
        Assert.Equal(0, Assert.Single((await ListAsync(c)).EnumerateArray()).GetProperty("matches").GetInt32());
    }

    [Fact]
    public async Task Apply_endpoint_returns_applied_and_summary_by_category_changes()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var account = await CreateAccountAsync(c);
        var mercado = await CategoryAsync(c, "Mercado");
        await ImportAsync(c, account, OfxTestFile.Checking()
            .Line(Ofx(5), "-50.00", Market, Fit1)
            .Line(Ofx(10), "250.00", "Transferencia recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", Fit2).Build());
        await CreateRuleAsync(c, "SUPERMERCADO EXEMPLO", mercado);

        var before = await JsonOf(await c.GetAsync("/api/summary?month=2026-10"));
        var balancesBefore = await (await c.GetAsync("/api/accounts")).Content.ReadAsStringAsync();
        Assert.All(before.GetProperty("byCategory").EnumerateArray(),
            e => Assert.Equal(JsonValueKind.Null, e.GetProperty("categoryId").ValueKind));

        var applied = await JsonOf(await c.PostAsync("/api/category-rules/apply"));
        Assert.Equal(1, applied.GetProperty("applied").GetInt32());
        Assert.Equal(0, (await JsonOf(await c.PostAsync("/api/category-rules/apply"))).GetProperty("applied").GetInt32());

        var after = await JsonOf(await c.GetAsync("/api/summary?month=2026-10"));
        foreach (var key in new[] { "income", "expenses", "net" })
            Assert.Equal(before.GetProperty(key).GetString(), after.GetProperty(key).GetString());
        var market = Assert.Single(after.GetProperty("byCategory").EnumerateArray(),
            e => e.GetProperty("categoryId").ValueKind != JsonValueKind.Null);
        Assert.Equal(mercado, market.GetProperty("categoryId").GetGuid());
        Assert.Equal("-50.00", market.GetProperty("total").GetString());
        var none = Assert.Single(after.GetProperty("byCategory").EnumerateArray(),
            e => e.GetProperty("categoryId").ValueKind == JsonValueKind.Null);
        Assert.Equal("250.00", none.GetProperty("total").GetString());
        Assert.Equal(balancesBefore, await (await c.GetAsync("/api/accounts")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Import_confirm_reports_categorized()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var account = await CreateAccountAsync(c);
        var mercado = await CategoryAsync(c, "Mercado");
        await CreateRuleAsync(c, "SUPERMERCADO EXEMPLO", mercado);

        var batch = await PreviewAsync(c, account, OfxTestFile.Checking().Line(Ofx(5), "-50.00", Market, Fit1).Build());
        var result = await ConfirmOkAsync(c, batch);
        Assert.Equal(1, result.GetProperty("categorized").GetInt32());
    }
}
