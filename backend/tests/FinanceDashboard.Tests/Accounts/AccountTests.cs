using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;

namespace FinanceDashboard.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class AccountTests(PostgresFixture postgres)
{
    private const string Password = "Correct-Horse-42!";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Create_list_update_archive_account()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        var created = await c.PostAsync("/api/accounts",
            new { name = "Nubank", type = "Checking", institution = "Nubank", externalIdentifier = "123" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var acc = await JsonOf(created);
        var id = acc.GetProperty("id").GetGuid();
        Assert.Equal("Checking", acc.GetProperty("type").GetString());
        Assert.False(acc.GetProperty("isArchived").GetBoolean());

        var list = await JsonOf(await c.GetAsync("/api/accounts"));
        Assert.Equal(1, list.GetArrayLength());

        var put = await c.PutAsync($"/api/accounts/{id}",
            new { name = "Nubank 2", institution = "Nu", externalIdentifier = (string?)null, isArchived = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var upd = await JsonOf(await c.GetAsync($"/api/accounts/{id}"));
        Assert.Equal("Nubank 2", upd.GetProperty("name").GetString());
        Assert.True(upd.GetProperty("isArchived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, upd.GetProperty("externalIdentifier").ValueKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Name_is_required_and_trimmed(string name)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        var bad = await c.PostAsync("/api/accounts", new { name, type = "Wallet", institution = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var tooLong = await c.PostAsync("/api/accounts", new { name = new string('a', 81), type = "Wallet", institution = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var ok = await c.PostAsync("/api/accounts", new { name = "  Carteira  ", type = "Wallet", institution = "x" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("Carteira", (await JsonOf(ok)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Type_cannot_change_on_update()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var id = (await JsonOf(await c.PostAsync("/api/accounts",
            new { name = "Cartao", type = "CreditCard", institution = "Nu" }))).GetProperty("id").GetGuid();

        var put = await c.PutAsync($"/api/accounts/{id}",
            new { name = "Cartao", type = "Checking", institution = "Nu", isArchived = false });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);

        var same = await c.PutAsync($"/api/accounts/{id}",
            new { name = "Cartao", type = "CreditCard", institution = "Nu", isArchived = false });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        var after = await JsonOf(await c.GetAsync($"/api/accounts/{id}"));
        Assert.Equal("CreditCard", after.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Other_users_account_is_404_on_get_and_put()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var id = (await JsonOf(await a.PostAsync("/api/accounts",
            new { name = "A", type = "Checking", institution = "x" }))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/accounts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsync($"/api/accounts/{id}",
            new { name = "X", institution = "x", isArchived = false })).StatusCode);
        Assert.Equal(0, (await JsonOf(await b.GetAsync("/api/accounts"))).GetArrayLength());
    }

    [Fact]
    public async Task Mutation_without_antiforgery_is_rejected()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        var response = await c.Raw.PostAsJsonAsync("/api/accounts", new { name = "A", type = "Wallet", institution = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        await using var f = await NewFactoryAsync();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
    }
}
