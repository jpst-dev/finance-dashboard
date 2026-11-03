using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.Transactions;

[Collection(PostgresCollection.Name)]
public sealed class TransactionTests(PostgresFixture postgres)
{
    private const string Password = "Correct-Horse-42!";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    internal static async Task<Guid> CreateAccountAsync(AuthedClient c, string type = "Checking", string name = "Conta")
    {
        var r = await c.PostAsync("/api/accounts", new { name, type, institution = "Banco" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    internal static async Task<Guid> CreateCategoryAsync(AuthedClient c)
    {
        var r = await c.PostAsync("/api/categories", new { name = "Mercado", kind = "Expense" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    internal static async Task<Guid> SeedImportedAsync(
        ApiFactory f, AuthedClient c, Guid accountId, DateOnly date, decimal amount, bool deleted = false,
        Guid? categoryId = null)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var t = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Date = date,
            Amount = amount,
            OriginalDescription = "orig",
            NormalizedDescription = "orig",
            State = TransactionState.PendingClassification,
            Origin = TransactionOrigin.Imported,
            SourceKey = "key-" + Guid.NewGuid().ToString("N"),
            Ordinal = 0,
            IsDeleted = deleted,
            CategoryId = categoryId,
        };
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task Create_list_filters_by_month_account_category_state()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var a1 = await CreateAccountAsync(c, name: "A1");
        var a2 = await CreateAccountAsync(c, name: "A2");
        var cat = await CreateCategoryAsync(c);

        var created = await c.PostAsync("/api/transactions",
            new { accountId = a1, date = "2026-09-10", amount = "-12.50", description = "  Padaria  ", categoryId = cat });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonOf(created);
        Assert.Equal("-12.50", body.GetProperty("amount").GetString());
        Assert.Equal("Padaria", body.GetProperty("description").GetString());
        Assert.Equal("Manual", body.GetProperty("origin").GetString());
        Assert.Equal("Normal", body.GetProperty("state").GetString());

        await c.PostAsync("/api/transactions", new { accountId = a1, date = "2026-10-01", amount = "5.00", description = "Out" });
        await c.PostAsync("/api/transactions",
            new { accountId = a2, date = "2026-09-20", amount = "7.00", description = "Outra conta", state = "PendingClassification" });

        async Task<int> Count(string q) =>
            (await JsonOf(await c.GetAsync("/api/transactions" + q))).GetProperty("items").GetArrayLength();

        Assert.Equal(3, await Count(""));
        Assert.Equal(2, await Count("?month=2026-09"));
        Assert.Equal(1, await Count($"?month=2026-09&accountId={a1}"));
        Assert.Equal(1, await Count($"?categoryId={cat}"));
        Assert.Equal(1, await Count("?state=PendingClassification"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/transactions?month=2026-13")).StatusCode);
    }

    [Fact]
    public async Task List_is_ordered_date_desc_then_id_desc_and_paginated()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        foreach (var d in new[] { "2026-09-01", "2026-09-03", "2026-09-03", "2026-09-02", "2026-09-05" })
        {
            await c.PostAsync("/api/transactions", new { accountId = acc, date = d, amount = "1.00", description = "x" });
        }

        var p1 = await JsonOf(await c.GetAsync("/api/transactions?page=1&size=2"));
        var p2 = await JsonOf(await c.GetAsync("/api/transactions?page=2&size=2"));
        var p3 = await JsonOf(await c.GetAsync("/api/transactions?page=3&size=2"));
        Assert.Equal(5, p1.GetProperty("total").GetInt32());
        Assert.Equal(2, p1.GetProperty("size").GetInt32());

        var all = new[] { p1, p2, p3 }.SelectMany(p => p.GetProperty("items").EnumerateArray()).ToList();
        Assert.Equal(5, all.Count);
        var keys = all.Select(i => (i.GetProperty("date").GetString()!, i.GetProperty("id").GetGuid())).ToList();
        Assert.Equal(
            keys.OrderByDescending(k => k.Item1, StringComparer.Ordinal).ThenByDescending(k => k.Item2).ToList(), keys);

        var big = await JsonOf(await c.GetAsync("/api/transactions?size=1000"));
        Assert.Equal(200, big.GetProperty("size").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/transactions?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/transactions?page=20000000&size=200")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/transactions?page=1000001")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/transactions?page=1000000&size=200")).StatusCode);
    }

    [Fact]
    public async Task Update_changes_state_and_category_of_imported_line()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var cat = await CreateCategoryAsync(c);
        var id = await SeedImportedAsync(f, c, acc, new DateOnly(2026, 9, 10), -30m);

        var put = await c.PutAsync($"/api/transactions/{id}",
            new { date = "2026-09-11", amount = "-31.00", description = "Mercado", categoryId = cat, state = "Normal" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await JsonOf(put);
        Assert.Equal("Normal", body.GetProperty("state").GetString());
        Assert.Equal(cat, body.GetProperty("categoryId").GetGuid());
        Assert.True(body.GetProperty("editedByUser").GetBoolean());
        Assert.Equal("Imported", body.GetProperty("origin").GetString());
        Assert.Equal("orig", body.GetProperty("originalDescription").GetString());
        Assert.Equal("Mercado", body.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Update_cannot_change_source_key()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var id = await SeedImportedAsync(f, c, acc, new DateOnly(2026, 9, 10), -30m);

        string? before;
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            before = (await db.Transactions.SingleAsync(t => t.Id == id)).SourceKey;
        }

        var put = await c.PutAsync($"/api/transactions/{id}", new
        {
            date = "2026-09-10", amount = "-30.00", description = "x", sourceKey = "hacked", ordinal = 9,
            origin = "Manual", batchId = Guid.NewGuid(), accountId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await using var db2 = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var t = await db2.Transactions.SingleAsync(x => x.Id == id);
        Assert.Equal(before, t.SourceKey);
        Assert.Equal(0, t.Ordinal);
        Assert.Equal(TransactionOrigin.Imported, t.Origin);
        Assert.Null(t.BatchId);
        Assert.Equal(acc, t.AccountId);
    }

    [Fact]
    public async Task Delete_manual_removes_row()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var id = (await JsonOf(await c.PostAsync("/api/transactions",
            new { accountId = acc, date = "2026-09-10", amount = "1.00", description = "x" }))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/transactions/{id}")).StatusCode);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.False(await db.Transactions.AnyAsync(t => t.Id == id));
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/transactions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_imported_hides_row_but_keeps_key()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var id = await SeedImportedAsync(f, c, acc, new DateOnly(2026, 9, 10), -30m);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/transactions/{id}")).StatusCode);

        Assert.Equal(0, (await JsonOf(await c.GetAsync("/api/transactions"))).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsync($"/api/transactions/{id}",
            new { date = "2026-09-10", amount = "1.00", description = "x" })).StatusCode);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var row = await db.Transactions.SingleAsync(t => t.Id == id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.SourceKey);
    }

    public static TheoryData<string> BadMoneyBodies => new()
    {
        "\"1,50\"", "1.5", "\"1.505\"", "\"abc\"", "\"1.234,56\"", "\"0.00\"", "\"0\"", "null", "\"12.50\\n\"",
        "\"10000000000000000.00\"",
    };

    [Theory]
    [MemberData(nameof(BadMoneyBodies))]
    public async Task Amount_validation_rejects_comma_number_zero_and_three_decimals(string amountJson)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        var raw = $"{{\"accountId\":\"{acc}\",\"date\":\"2026-09-10\",\"amount\":{amountJson},\"description\":\"x\"}}";
        var post = await RawJsonAsync(c, HttpMethod.Post, "/api/transactions", raw);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);

        var okId = (await JsonOf(await c.PostAsync("/api/transactions",
            new { accountId = acc, date = "2026-09-10", amount = "1.00", description = "x" }))).GetProperty("id").GetGuid();
        var put = await RawJsonAsync(c, HttpMethod.Put, $"/api/transactions/{okId}",
            $"{{\"date\":\"2026-09-10\",\"amount\":{amountJson},\"description\":\"x\"}}");
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);

        var list = await JsonOf(await c.GetAsync("/api/transactions"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("1.00", list.GetProperty("items")[0].GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Description_length_is_validated()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        foreach (var d in new[] { "", "   ", new string('a', 301) })
        {
            var r = await c.PostAsync("/api/transactions",
                new { accountId = acc, date = "2026-09-10", amount = "1.00", description = d });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        var ok = await c.PostAsync("/api/transactions",
            new { accountId = acc, date = "2026-09-10", amount = "1.00", description = new string('a', 300) });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Archived_account_rejects_new_transactions()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await c.PutAsync($"/api/accounts/{acc}", new { name = "Conta", institution = "Banco", isArchived = true });

        var r = await c.PostAsync("/api/transactions",
            new { accountId = acc, date = "2026-09-10", amount = "1.00", description = "x" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Account_put_without_is_archived_keeps_archived_flag()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await c.PutAsync($"/api/accounts/{acc}", new { name = "Conta", institution = "Banco", isArchived = true });

        var put = await c.PutAsync($"/api/accounts/{acc}", new { name = "Renomeada", institution = "Banco" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.True((await JsonOf(await c.GetAsync($"/api/accounts/{acc}"))).GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task Other_users_transaction_is_404_for_get_put_delete()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var accA = await CreateAccountAsync(a);
        var accB = await CreateAccountAsync(b);
        var catA = await CreateCategoryAsync(a);
        var id = (await JsonOf(await a.PostAsync("/api/transactions",
            new { accountId = accA, date = "2026-09-10", amount = "1.00", description = "x" }))).GetProperty("id").GetGuid();

        Assert.Equal(0, (await JsonOf(await b.GetAsync("/api/transactions"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsync($"/api/transactions/{id}",
            new { date = "2026-09-10", amount = "2.00", description = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/transactions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync("/api/transactions",
            new { accountId = accA, date = "2026-09-10", amount = "1.00", description = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync("/api/transactions",
            new { accountId = accB, date = "2026-09-10", amount = "1.00", description = "x", categoryId = catA })).StatusCode);
        Assert.Equal(1, (await JsonOf(await a.GetAsync("/api/transactions"))).GetProperty("total").GetInt32());
    }

    private static async Task<HttpResponseMessage> RawJsonAsync(AuthedClient c, HttpMethod method, string url, string json)
    {
        var token = await Auth.AuthTestHelpers.GetCsrfTokenAsync(c.Raw);
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Auth.AuthTestHelpers.AntiforgeryHeader, token);
        return await c.Raw.SendAsync(request);
    }
}
