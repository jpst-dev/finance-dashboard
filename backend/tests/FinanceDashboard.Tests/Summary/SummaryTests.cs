using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;

namespace FinanceDashboard.Tests.Summary;

[Collection(PostgresCollection.Name)]
public sealed class SummaryTests(PostgresFixture postgres)
{
    private const string Password = "Correct-Horse-42!";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateAccountAsync(AuthedClient c, string type = "Checking", string name = "Conta")
    {
        var r = await c.PostAsync("/api/accounts", new { name, type, institution = "Banco" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCategoryAsync(AuthedClient c, string name)
    {
        var r = await c.PostAsync("/api/categories", new { name, kind = "Expense" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddAsync(
        AuthedClient c, Guid account, string date, string amount, Guid? category = null, bool pending = false)
    {
        var r = await c.PostAsync("/api/transactions", new
        {
            accountId = account, date, amount, description = "Linha", categoryId = category,
            state = pending ? "PendingClassification" : "Normal",
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Summary_for_month_matches_calculator_rules()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await CreateAccountAsync(c, "Checking", "CC");
        var card = await CreateAccountAsync(c, "CreditCard", "Cartao");
        var market = await CreateCategoryAsync(c, "Mercado");

        await AddAsync(c, checking, "2026-09-05", "1000.00");
        await AddAsync(c, checking, "2026-09-06", "-200.50", market);
        await AddAsync(c, card, "2026-09-07", "-50.00", market);
        await AddAsync(c, card, "2026-09-08", "10.00"); // card refund nets into expenses

        var r = await c.GetAsync("/api/summary?month=2026-09");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await JsonOf(r);
        Assert.Equal("2026-09", body.GetProperty("month").GetString());
        Assert.Equal("1000.00", body.GetProperty("income").GetString());
        Assert.Equal("-240.50", body.GetProperty("expenses").GetString());
        Assert.Equal("759.50", body.GetProperty("net").GetString());
        Assert.Equal(0, body.GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal("0.00", body.GetProperty("pending").GetProperty("total").GetString());

        var by = body.GetProperty("byCategory").EnumerateArray().ToList();
        Assert.Equal(2, by.Count);
        var m = by.Single(e => e.GetProperty("categoryId").ValueKind == JsonValueKind.String);
        Assert.Equal(market, m.GetProperty("categoryId").GetGuid());
        Assert.Equal("Mercado", m.GetProperty("categoryName").GetString());
        Assert.Equal("-250.50", m.GetProperty("total").GetString());
        var none = by.Single(e => e.GetProperty("categoryId").ValueKind == JsonValueKind.Null);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("categoryName").ValueKind);
        Assert.Equal("1010.00", none.GetProperty("total").GetString());

        var onlyCard = await JsonOf(await c.GetAsync($"/api/summary?month=2026-09&accountId={card}"));
        Assert.Equal("0.00", onlyCard.GetProperty("income").GetString());
        Assert.Equal("-40.00", onlyCard.GetProperty("expenses").GetString());
    }

    [Fact]
    public async Task Pending_are_excluded_but_counted()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await AddAsync(c, acc, "2026-09-05", "100.00");
        await AddAsync(c, acc, "2026-09-06", "-30.00", pending: true);
        await AddAsync(c, acc, "2026-09-07", "-20.00", pending: true);

        var body = await JsonOf(await c.GetAsync("/api/summary?month=2026-09"));
        Assert.Equal("100.00", body.GetProperty("income").GetString());
        Assert.Equal("0.00", body.GetProperty("expenses").GetString());
        Assert.Equal(2, body.GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal("-50.00", body.GetProperty("pending").GetProperty("total").GetString());
    }

    [Fact]
    public async Task Deleted_lines_are_excluded()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await AddAsync(c, acc, "2026-09-05", "-10.00");
        var imported = await TransactionTests_SeedImported(f, c, acc, new DateOnly(2026, 9, 9), -99m, deleted: true);
        Assert.NotEqual(Guid.Empty, imported);
        var manual = await AddAsync(c, acc, "2026-09-06", "-5.00");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/transactions/{manual}")).StatusCode);

        var body = await JsonOf(await c.GetAsync("/api/summary?month=2026-09"));
        Assert.Equal("-10.00", body.GetProperty("expenses").GetString());
        Assert.Equal(0, body.GetProperty("pending").GetProperty("count").GetInt32());
    }

    private static Task<Guid> TransactionTests_SeedImported(
        ApiFactory f, AuthedClient c, Guid acc, DateOnly date, decimal amount, bool deleted) =>
        Transactions.TransactionTests.SeedImportedAsync(f, c, acc, date, amount, deleted);

    [Fact]
    public async Task Other_month_lines_are_excluded_boundaries()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await AddAsync(c, acc, "2026-09-30", "-1.00");  // before October
        await AddAsync(c, acc, "2026-10-01", "-2.00");  // first day
        await AddAsync(c, acc, "2026-10-31", "-4.00");  // last day
        await AddAsync(c, acc, "2026-11-01", "-8.00");  // after

        var oct = await JsonOf(await c.GetAsync("/api/summary?month=2026-10"));
        Assert.Equal("-6.00", oct.GetProperty("expenses").GetString());
        var nov = await JsonOf(await c.GetAsync("/api/summary?month=2026-11"));
        Assert.Equal("-8.00", nov.GetProperty("expenses").GetString());
    }

    [Fact]
    public async Task February_boundaries_including_leap_year()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await AddAsync(c, acc, "2026-02-28", "-1.00");
        await AddAsync(c, acc, "2026-03-01", "-2.00");
        await AddAsync(c, acc, "2028-02-29", "-4.00");
        await AddAsync(c, acc, "2028-03-01", "-8.00");

        Assert.Equal("-1.00", (await JsonOf(await c.GetAsync("/api/summary?month=2026-02"))).GetProperty("expenses").GetString());
        Assert.Equal("-4.00", (await JsonOf(await c.GetAsync("/api/summary?month=2028-02"))).GetProperty("expenses").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?month=")]
    [InlineData("?month=2026-13")]
    [InlineData("?month=2026-9")]
    [InlineData("?month=abc")]
    [InlineData("?month=2026-09-01")]
    public async Task Invalid_month_is_400(string query)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/summary" + query)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_is_401()
    {
        await using var f = await NewFactoryAsync();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/summary?month=2026-09")).StatusCode);
    }

    [Fact]
    public async Task Only_pending_month_returns_zeros_with_count()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await AddAsync(c, acc, "2026-09-05", "-30.00", pending: true);

        var body = await JsonOf(await c.GetAsync("/api/summary?month=2026-09"));
        Assert.Equal("0.00", body.GetProperty("income").GetString());
        Assert.Equal("0.00", body.GetProperty("expenses").GetString());
        Assert.Equal("0.00", body.GetProperty("net").GetString());
        Assert.Equal(0, body.GetProperty("byCategory").GetArrayLength());
        Assert.Equal(1, body.GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal("-30.00", body.GetProperty("pending").GetProperty("total").GetString());
    }

    [Fact]
    public async Task Other_users_data_never_counted()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var accA = await CreateAccountAsync(a);
        var accB = await CreateAccountAsync(b);
        await AddAsync(a, accA, "2026-09-05", "-10.00");
        await AddAsync(b, accB, "2026-09-05", "-777.00");

        var body = await JsonOf(await a.GetAsync("/api/summary?month=2026-09"));
        Assert.Equal("-10.00", body.GetProperty("expenses").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/summary?month=2026-09&accountId={accB}")).StatusCode);
    }
}
