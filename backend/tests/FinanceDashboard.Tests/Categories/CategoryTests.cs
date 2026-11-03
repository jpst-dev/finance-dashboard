using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;

namespace FinanceDashboard.Tests.Categories;

[Collection(PostgresCollection.Name)]
public sealed class CategoryTests(PostgresFixture postgres)
{
    private const string Password = "Correct-Horse-42!";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task New_user_has_default_categories()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await DefaultCategories.EnsureForAllUsersAsync(f.Services);

        var list = await JsonOf(await c.GetAsync("/api/categories"));
        Assert.Equal(13, list.GetArrayLength());
        Assert.Contains(list.EnumerateArray(), x =>
            x.GetProperty("name").GetString() == "Salário"
            && x.GetProperty("kind").GetString() == "Income"
            && x.GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task Create_rename_delete_category()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        var created = await c.PostAsync("/api/categories", new { name = "  Pets ", kind = "Expense" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var cat = await JsonOf(created);
        var id = cat.GetProperty("id").GetGuid();
        Assert.Equal("Pets", cat.GetProperty("name").GetString());
        Assert.False(cat.GetProperty("isDefault").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/categories", new { name = " ", kind = "Expense" })).StatusCode);

        var put = await c.PutAsync($"/api/categories/{id}", new { name = "Bichos" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("Bichos", (await JsonOf(put)).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(0, (await JsonOf(await c.GetAsync("/api/categories"))).GetArrayLength());
    }

    [Fact]
    public async Task Other_users_category_is_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var id = (await JsonOf(await a.PostAsync("/api/categories", new { name = "Mine", kind = "Income" }))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsync($"/api/categories/{id}", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/categories/{id}")).StatusCode);
        Assert.Equal(1, (await JsonOf(await a.GetAsync("/api/categories"))).GetArrayLength());
    }
}
