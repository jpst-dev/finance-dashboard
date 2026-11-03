using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Auth;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Classifying the new lines of a preview on confirm (<c>overrides</c>).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportOverridesTests(PostgresFixture postgres)
{
    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static Task<HttpResponseMessage> ConfirmWithAsync(AuthedClient c, Guid batchId, params object[] overrides) =>
        c.PostAsync($"/api/imports/{batchId}/confirm", new { acknowledgeBlockingWarnings = false, overrides });

    private static async Task<Guid> CreateCategoryAsync(AuthedClient c, string name, string kind = "Expense")
    {
        var r = await c.PostAsync("/api/categories", new { name, kind });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    private static async Task AssertNothingWrittenAsync(ApiFactory f, AuthedClient c, Guid batchId)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(0, await db.Checkpoints.CountAsync());
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(ImportBatchState.Preview, batch.State);
        Assert.NotNull(batch.PreviewJson);
    }

    [Fact]
    public async Task Preview_lines_expose_source_key()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        var r = await UploadAsync(c, acc, ThreeLines().Build());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var keys = (await JsonOf(r)).GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("sourceKey").GetString()).ToList();
        Assert.Equal([Fit1, Fit2, Fit3], keys);
    }

    [Fact]
    public async Task Override_sets_state_and_category_of_new_line()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var income = await CreateCategoryAsync(c, "Pix recebido", "Income");
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        // Fit2 is PendingClassification by the Nubank rules.
        var r = await ConfirmWithAsync(c, batchId, new { sourceKey = Fit2, state = "Normal", categoryId = income });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await JsonOf(r);
        Assert.Equal(batchId, body.GetProperty("batchId").GetGuid());
        Assert.Equal(3, body.GetProperty("created").GetInt32());
        Assert.Equal(0, body.GetProperty("skipped").GetInt32());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var line = await db.Transactions.SingleAsync(t => t.SourceKey == Fit2);
        Assert.Equal(TransactionState.Normal, line.State);
        Assert.Equal(income, line.CategoryId);
        Assert.Equal(c.UserId, line.UserId);
        Assert.False(line.EditedByUser);
        // Lines without an override keep the rules' classification.
        var others = await db.Transactions.Where(t => t.SourceKey != Fit2).OrderBy(t => t.Date).ToListAsync();
        Assert.Equal(TransactionState.Normal, others[0].State);
        Assert.Equal(TransactionState.PendingClassification, others[1].State);
        Assert.All(others, t => Assert.Null(t.CategoryId));
    }

    [Fact]
    public async Task Override_for_existing_line_is_ignored_and_existing_line_untouched()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var cat = await CreateCategoryAsync(c, "Mercado");
        var first = OfxTestFile.Checking()
            .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito - Padaria Ficticia", Fit1)
            .Build();
        var firstBatch = await PreviewAsync(c, acc, first);
        await ConfirmOkAsync(c, firstBatch);

        // Fit1 is already in the account when this preview is made: it is listed as existing.
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());
        var r = await ConfirmWithAsync(c, batchId,
            new { sourceKey = Fit1, state = "PendingClassification", categoryId = cat });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await JsonOf(r);
        Assert.Equal(2, body.GetProperty("created").GetInt32());
        Assert.Equal(1, body.GetProperty("skipped").GetInt32());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var existing = await db.Transactions.SingleAsync(t => t.SourceKey == Fit1);
        Assert.Equal(firstBatch, existing.BatchId);
        Assert.Equal(TransactionState.Normal, existing.State);
        Assert.Null(existing.CategoryId);
        Assert.False(existing.EditedByUser);
    }

    [Fact]
    public async Task Override_for_line_that_became_existing_after_preview_is_ignored()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var cat = await CreateCategoryAsync(c, "Mercado");

        // Previewed while Fit1 is still new; another file records Fit1 before this preview is confirmed.
        var wider = await PreviewAsync(c, acc, ThreeLines().Build());
        var other = await PreviewAsync(c, acc, OfxTestFile.Checking()
            .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito - Padaria Ficticia", Fit1)
            .Build());
        await ConfirmOkAsync(c, other);

        var r = await ConfirmWithAsync(c, wider,
            new { sourceKey = Fit1, state = "PendingClassification", categoryId = cat },
            new { sourceKey = Fit3, state = "Normal", categoryId = cat });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await JsonOf(r);
        Assert.Equal(2, body.GetProperty("created").GetInt32());
        Assert.Equal(1, body.GetProperty("skipped").GetInt32());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var existing = await db.Transactions.SingleAsync(t => t.SourceKey == Fit1);
        Assert.Equal(other, existing.BatchId);
        Assert.Equal(TransactionState.Normal, existing.State);
        Assert.Null(existing.CategoryId);
        var created = await db.Transactions.SingleAsync(t => t.SourceKey == Fit3);
        Assert.Equal(wider, created.BatchId);
        Assert.Equal(TransactionState.Normal, created.State);
        Assert.Equal(cat, created.CategoryId);
    }

    [Fact]
    public async Task Unknown_source_key_is_422_and_nothing_written()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var r = await ConfirmWithAsync(c, batchId,
            new { sourceKey = Fit1, state = "Normal" },
            new { sourceKey = "not-in-this-preview", state = "Normal" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_override", await ErrorOf(r));
        await AssertNothingWrittenAsync(f, c, batchId);

        // The preview is still usable.
        Assert.Equal(3, (await ConfirmOkAsync(c, batchId)).GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task Invalid_state_value_is_400()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var r = await ConfirmWithAsync(c, batchId, new { sourceKey = Fit1, state = "Bogus" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        await AssertNothingWrittenAsync(f, c, batchId);
    }

    [Fact]
    public async Task Duplicate_source_key_in_overrides_is_422()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var r = await ConfirmWithAsync(c, batchId,
            new { sourceKey = Fit2, state = "Normal" },
            new { sourceKey = Fit2, state = "PendingClassification" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_override", await ErrorOf(r));
        await AssertNothingWrittenAsync(f, c, batchId);
    }

    [Fact]
    public async Task Other_users_category_is_422()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var acc = await CreateAccountAsync(a);
        var foreign = await CreateCategoryAsync(b, "Categoria de B");
        var batchId = await PreviewAsync(a, acc, ThreeLines().Build());

        var r = await ConfirmWithAsync(a, batchId, new { sourceKey = Fit1, categoryId = foreign });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_override", await ErrorOf(r));
        await AssertNothingWrittenAsync(f, a, batchId);
    }

    [Fact]
    public async Task Unknown_category_is_422()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var r = await ConfirmWithAsync(c, batchId, new { sourceKey = Fit1, categoryId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_override", await ErrorOf(r));
        await AssertNothingWrittenAsync(f, c, batchId);
    }

    [Fact]
    public async Task State_and_category_are_independent()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var cat = await CreateCategoryAsync(c, "Contas");
        const string fit4 = "44444444-4444-4444-8444-444444444444";
        var file = ThreeLines()
            .Line("20261025000000[-3:BRT]", "-5.00", "Compra no debito - Banca Ficticia", fit4)
            .Build();
        var batchId = await PreviewAsync(c, acc, file);

        var r = await ConfirmWithAsync(c, batchId,
            // Category only: the rules' state (Normal) stays.
            new { sourceKey = Fit1, categoryId = cat },
            // State only: Normal without a category.
            new { sourceKey = Fit2, state = "Normal" },
            // Pending with a category.
            new { sourceKey = Fit3, state = "PendingClassification", categoryId = cat },
            // State only, the other way: Normal → Pending, still no category.
            new { sourceKey = fit4, state = "PendingClassification" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var byKey = await db.Transactions.ToDictionaryAsync(t => t.SourceKey!);
        Assert.Equal((TransactionState.Normal, cat), (byKey[Fit1].State, byKey[Fit1].CategoryId));
        Assert.Equal((TransactionState.Normal, (Guid?)null), (byKey[Fit2].State, byKey[Fit2].CategoryId));
        Assert.Equal((TransactionState.PendingClassification, cat), (byKey[Fit3].State, byKey[Fit3].CategoryId));
        Assert.Equal((TransactionState.PendingClassification, (Guid?)null), (byKey[fit4].State, byKey[fit4].CategoryId));
    }

    [Fact]
    public async Task No_overrides_keeps_current_behavior()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var withoutField = await PreviewAsync(c, acc, ThreeLines().Build());

        // Body without "overrides" (S1a clients) and with an empty list behave the same.
        var r1 = await ConfirmAsync(c, withoutField);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(3, (await JsonOf(r1)).GetProperty("created").GetInt32());

        var acc2 = await CreateAccountAsync(c);
        var emptyList = await PreviewAsync(c, acc2, OfxTestFile.Checking()
            .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito - Padaria Ficticia", "55555555-5555-4555-8555-555555555555")
            .Build());
        var r2 = await ConfirmWithAsync(c, emptyList);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(1, (await JsonOf(r2)).GetProperty("created").GetInt32());

        var r3 = await c.PostAsync($"/api/imports/{emptyList}/confirm", new { acknowledgeBlockingWarnings = false, overrides = (object?)null });
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
        Assert.Equal("already_confirmed", await ErrorOf(r3));

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var lines = await db.Transactions.Where(t => t.AccountId == acc).OrderBy(t => t.Date).ToListAsync();
        Assert.Equal([TransactionState.Normal, TransactionState.Normal, TransactionState.PendingClassification],
            lines.Select(t => t.State));
        Assert.All(await db.Transactions.ToListAsync(), t => Assert.Null(t.CategoryId));
    }

    [Fact]
    public async Task Overrides_are_not_logged()
    {
        var logs = new CapturingLoggerProvider();
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync(), new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Trace",
        }) { LoggerProvider = logs };
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var cat = await CreateCategoryAsync(c, "Mercado");

        const string keyMarker = "OVRKEYMARKER-QZ71";
        const string unknownMarker = "OVRUNKNOWNMARKER-QZ72";
        var file = OfxTestFile.Checking()
            .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito - Padaria Ficticia", keyMarker)
            .Build();
        var batchId = await PreviewAsync(c, acc, file);

        var bad = await ConfirmWithAsync(c, batchId, new { sourceKey = unknownMarker, state = "Normal", categoryId = cat });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var ok = await ConfirmWithAsync(c, batchId, new { sourceKey = keyMarker, state = "PendingClassification", categoryId = cat });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        Assert.NotEmpty(logs.Entries);
        foreach (var marker in new[] { keyMarker, unknownMarker, cat.ToString() })
        {
            Assert.DoesNotContain(logs.Entries, e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }
}
