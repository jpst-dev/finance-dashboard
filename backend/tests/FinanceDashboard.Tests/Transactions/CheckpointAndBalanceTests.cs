using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;

namespace FinanceDashboard.Tests.Transactions;

[Collection(PostgresCollection.Name)]
public sealed class CheckpointAndBalanceTests(PostgresFixture postgres)
{
    private const string Password = "Correct-Horse-42!";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> BalanceAsync(AuthedClient c, Guid id) =>
        (await JsonOf(await c.GetAsync($"/api/accounts/{id}"))).GetProperty("balance").GetString()!;

    private static Task AddAsync(AuthedClient c, Guid acc, string date, string amount) =>
        c.PostAsync("/api/transactions", new { accountId = acc, date, amount, description = "x" });

    [Fact]
    public async Task Balance_without_checkpoint_sums_visible_lines()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await TransactionTests.CreateAccountAsync(c);
        Assert.Equal("0.00", await BalanceAsync(c, acc));

        await AddAsync(c, acc, "2026-09-01", "100.00");
        await AddAsync(c, acc, "2026-09-02", "-30.25");
        Assert.Equal("69.75", await BalanceAsync(c, acc));

        var list = await JsonOf(await c.GetAsync("/api/accounts"));
        Assert.Equal("69.75", list[0].GetProperty("balance").GetString());
    }

    [Fact]
    public async Task Balance_uses_latest_checkpoint_and_only_later_lines()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await TransactionTests.CreateAccountAsync(c);
        await AddAsync(c, acc, "2026-10-31", "50.00");
        await AddAsync(c, acc, "2026-11-01", "-20.00");

        var cp = await c.PostAsync($"/api/accounts/{acc}/checkpoints", new { date = "2026-10-31", amount = "100.00" });
        Assert.Equal(HttpStatusCode.Created, cp.StatusCode);
        Assert.Equal("Manual", (await JsonOf(cp)).GetProperty("origin").GetString());
        Assert.Equal("80.00", await BalanceAsync(c, acc));

        await c.PostAsync($"/api/accounts/{acc}/checkpoints", new { date = "2026-10-01", amount = "999.00" });
        Assert.Equal("80.00", await BalanceAsync(c, acc));

        var list = await JsonOf(await c.GetAsync($"/api/accounts/{acc}/checkpoints"));
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("2026-10-31", list[0].GetProperty("date").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/accounts/{acc}/checkpoints",
            new { date = "2026-10-31", amount = "1,5" })).StatusCode);
    }

    [Fact]
    public async Task Deleted_lines_do_not_affect_balance()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await TransactionTests.CreateAccountAsync(c);
        await TransactionTests.SeedImportedAsync(f, c, acc, new DateOnly(2026, 9, 1), 100m);
        await TransactionTests.SeedImportedAsync(f, c, acc, new DateOnly(2026, 9, 2), 40m, deleted: true);
        var live = await TransactionTests.SeedImportedAsync(f, c, acc, new DateOnly(2026, 9, 3), 10m);
        Assert.Equal("110.00", await BalanceAsync(c, acc));

        await c.DeleteAsync($"/api/transactions/{live}");
        Assert.Equal("100.00", await BalanceAsync(c, acc));
    }

    [Fact]
    public async Task Card_balance_is_negative_debt()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var card = await TransactionTests.CreateAccountAsync(c, "CreditCard", "Cartao");
        await AddAsync(c, card, "2026-09-05", "-200.00");
        await AddAsync(c, card, "2026-09-06", "50.00");
        Assert.Equal("-150.00", await BalanceAsync(c, card));
    }

    [Fact]
    public async Task Other_users_checkpoints_are_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var acc = await TransactionTests.CreateAccountAsync(a);
        await a.PostAsync($"/api/accounts/{acc}/checkpoints", new { date = "2026-10-31", amount = "100.00" });

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/accounts/{acc}/checkpoints")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/accounts/{acc}/checkpoints",
            new { date = "2026-10-31", amount = "5.00" })).StatusCode);
        Assert.Equal(1, (await JsonOf(await a.GetAsync($"/api/accounts/{acc}/checkpoints"))).GetArrayLength());
    }
}
