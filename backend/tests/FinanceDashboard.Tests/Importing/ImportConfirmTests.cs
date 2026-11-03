using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

[Collection(PostgresCollection.Name)]
public sealed class ImportConfirmTests(PostgresFixture postgres)
{
    private async Task<ApiFactory> NewFactoryAsync(TimeProvider? time = null) =>
        new(await postgres.CreateDatabaseAsync()) { TimeProvider = time };

    [Fact]
    public async Task Confirm_creates_transactions_with_state_counterparty_and_checkpoint()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var result = await ConfirmOkAsync(c, batchId);

        Assert.Equal(batchId, result.GetProperty("batchId").GetGuid());
        Assert.Equal(3, result.GetProperty("created").GetInt32());
        Assert.Equal(0, result.GetProperty("skipped").GetInt32());

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var lines = await db.Transactions.OrderBy(t => t.Date).ToListAsync();
            Assert.Equal(3, lines.Count);
            Assert.All(lines, t =>
            {
                Assert.Equal(acc, t.AccountId);
                Assert.Equal(c.UserId, t.UserId);
                Assert.Equal(TransactionOrigin.Imported, t.Origin);
                Assert.Equal(batchId, t.BatchId);
                Assert.Equal(0, t.Ordinal);
                Assert.Equal(t.Date, t.PurchaseDate);
                Assert.Null(t.CategoryId);
                Assert.False(t.IsDeleted);
                Assert.False(t.EditedByUser);
            });

            Assert.Equal(Fit1, lines[0].SourceKey);
            Assert.Equal(new DateOnly(2026, 10, 5), lines[0].Date);
            Assert.Equal(-10m, lines[0].Amount);
            Assert.Equal(TransactionState.Normal, lines[0].State);
            Assert.Equal("Compra no debito -   Padaria   Ficticia", lines[0].OriginalDescription);
            Assert.Equal("Compra no debito - Padaria Ficticia", lines[0].NormalizedDescription);
            Assert.Null(lines[0].CounterpartyName);

            Assert.Equal(Fit2, lines[1].SourceKey);
            Assert.Equal(250m, lines[1].Amount);
            Assert.Equal(TransactionState.PendingClassification, lines[1].State);
            Assert.Equal("FULANO FICTICIO", lines[1].CounterpartyName);
            Assert.Equal("***.123.456-**", lines[1].CounterpartyDocument);

            Assert.Equal(Fit3, lines[2].SourceKey);
            Assert.Equal(-40.50m, lines[2].Amount);
            Assert.Equal(TransactionState.PendingClassification, lines[2].State);

            var checkpoint = await db.Checkpoints.SingleAsync();
            Assert.Equal(acc, checkpoint.AccountId);
            Assert.Equal(new DateOnly(2026, 10, 31), checkpoint.Date);
            Assert.Equal(100.50m, checkpoint.Amount);
            Assert.Equal(CheckpointOrigin.LedgerBal, checkpoint.Origin);
            Assert.Equal(batchId, checkpoint.BatchId);

            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchState.Confirmed, batch.State);
            Assert.Null(batch.PreviewJson);
            Assert.Equal(3, batch.NewCount);
        }

        var got = await JsonOf(await c.GetAsync($"/api/imports/{batchId}"));
        Assert.Equal("Confirmed", got.GetProperty("state").GetString());
        Assert.Equal(0, got.GetProperty("editedCount").GetInt32());
        // Balance comes from the new checkpoint (no line is after 2026-10-31).
        var account = await JsonOf(await c.GetAsync($"/api/accounts/{acc}"));
        Assert.Equal("100.50", account.GetProperty("balance").GetString());
    }

    [Fact]
    public async Task Confirm_does_not_touch_existing_edited_lines()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var first = OfxTestFile.Checking()
            .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito - Padaria Ficticia", Fit1)
            .Line("20261010000000[-3:BRT]", "250.00", "Transferencia recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", Fit2)
            .Build();

        // The wider file is previewed before anything exists: confirm must re-check against the current state.
        var wider = await PreviewAsync(c, acc, ThreeLines().Build());
        var firstBatch = await PreviewAsync(c, acc, first);
        Assert.Equal(2, (await ConfirmOkAsync(c, firstBatch)).GetProperty("created").GetInt32());

        Guid editedId, deletedId;
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            editedId = await db.Transactions.Where(t => t.SourceKey == Fit1).Select(t => t.Id).SingleAsync();
            deletedId = await db.Transactions.Where(t => t.SourceKey == Fit2).Select(t => t.Id).SingleAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/transactions/{editedId}",
            new { date = "2026-10-06", amount = "-11.00", description = "Padaria editada", state = "PendingClassification" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/transactions/{deletedId}")).StatusCode);

        var result = await ConfirmOkAsync(c, wider);
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(2, result.GetProperty("skipped").GetInt32());

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(3, await db.Transactions.CountAsync());
            var edited = await db.Transactions.SingleAsync(t => t.Id == editedId);
            Assert.Equal(new DateOnly(2026, 10, 6), edited.Date);
            Assert.Equal(-11m, edited.Amount);
            Assert.Equal("Padaria editada", edited.NormalizedDescription);
            Assert.Equal(TransactionState.PendingClassification, edited.State);
            Assert.True(edited.EditedByUser);
            Assert.Equal(firstBatch, edited.BatchId);

            var deleted = await db.Transactions.SingleAsync(t => t.Id == deletedId);
            Assert.True(deleted.IsDeleted);
            Assert.Equal(firstBatch, deleted.BatchId);

            Assert.Equal(wider, (await db.Transactions.SingleAsync(t => t.SourceKey == Fit3)).BatchId);
            // Same LEDGERBAL (account, date, amount) in both files: one checkpoint only.
            Assert.Equal(firstBatch, (await db.Checkpoints.SingleAsync()).BatchId);
        }
    }

    [Fact]
    public async Task Confirm_with_blocking_warning_requires_acknowledgement()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var mismatched = await CreateAccountAsync(c, externalId: "ACC-9999");
        var batchId = await PreviewAsync(c, mismatched, ThreeLines().Build());

        var refused = await ConfirmAsync(c, batchId);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await JsonOf(refused);
        Assert.Equal("blocking_warnings", body.GetProperty("error").GetString());
        var warning = Assert.Single(body.GetProperty("warnings").EnumerateArray());
        Assert.Equal("AccountIdentifierMismatch", warning.GetProperty("code").GetString());
        Assert.Equal("blocking", warning.GetProperty("severity").GetString());
        // No body at all means no acknowledgement.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/imports/{batchId}/confirm")).StatusCode);

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(0, await db.Transactions.CountAsync());
            Assert.Equal(0, await db.Checkpoints.CountAsync());
            Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.SingleAsync()).State);
        }

        Assert.Equal(3, (await ConfirmOkAsync(c, batchId, acknowledge: true)).GetProperty("created").GetInt32());

        // A blocking condition that appears after the preview is caught by the re-validation on confirm.
        var acc1 = await CreateAccountAsync(c);
        var acc2 = await CreateAccountAsync(c);
        var file = OfxTestFile.Checking().AccountId("ACC-0007")
            .Line("20261105000000[-3:BRT]", "-3.00", "Compra no debito - Loja", "44444444-4444-4444-8444-444444444444")
            .Period("20261101000000[-3:BRT]", "20261130000000[-3:BRT]").NoLedger();
        var p1 = await PreviewAsync(c, acc1, file.Build());
        var p2 = await PreviewAsync(c, acc2, file.AccountId("ACC-0008").Build());
        await ConfirmOkAsync(c, p2);

        var late = await ConfirmAsync(c, p1);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("FitidInOtherAccount",
            Assert.Single((await JsonOf(late)).GetProperty("warnings").EnumerateArray()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Confirm_expired_preview_is_410()
    {
        var time = TestClock.StartingNow(); // follows the real now: see TestClock
        await using var f = await NewFactoryAsync(time);
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        time.Advance(TimeSpan.FromHours(24));
        var r = await ConfirmAsync(c, batchId);

        Assert.Equal(HttpStatusCode.Gone, r.StatusCode);
        Assert.Equal("preview_expired", await ErrorOf(r));
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(0, await db.Checkpoints.CountAsync());
    }

    [Fact]
    public async Task Other_users_batch_confirm_is_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var accA = await CreateAccountAsync(a);
        var batchId = await PreviewAsync(a, accA, ThreeLines().Build());

        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(b, batchId, acknowledge: true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(b, Guid.NewGuid())).StatusCode);

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, null))
        {
            Assert.Equal(0, await db.Transactions.IgnoreQueryFilters().CountAsync());
            Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.IgnoreQueryFilters().SingleAsync()).State);
        }

        Assert.Equal(3, (await ConfirmOkAsync(a, batchId)).GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task Confirm_twice_second_is_409_or_noop()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());
        await ConfirmOkAsync(c, batchId);

        var second = await ConfirmAsync(c, batchId);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_confirmed", await ErrorOf(second));

        // The same file in a new preview: already imported, nothing added.
        var again = await PreviewAsync(c, acc, ThreeLines().Build());
        var third = await ConfirmAsync(c, again);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("already_imported", await ErrorOf(third));

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
        Assert.Equal(1, await db.Checkpoints.CountAsync());
        Assert.Equal(1, await db.ImportBatches.CountAsync(b => b.State == ImportBatchState.Confirmed));
    }

    [Fact]
    public async Task Confirm_truncates_memos_longer_than_500_chars()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var memo = "Compra no debito -  " + new string('x', 700);
        var batchId = await PreviewAsync(c, acc,
            OfxTestFile.Checking().Line("20261005000000[-3:BRT]", "-1.00", memo, Fit1).Build());

        Assert.Equal(1, (await ConfirmOkAsync(c, batchId)).GetProperty("created").GetInt32());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var t = await db.Transactions.SingleAsync();
        Assert.Equal(memo[..500], t.OriginalDescription);
        Assert.Equal(500, t.NormalizedDescription.Length);
        Assert.StartsWith("Compra no debito - xxx", t.NormalizedDescription);
    }

    [Fact]
    public async Task Confirm_with_amount_over_two_decimals_is_422_and_writes_nothing()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines()
            .Line("20261021000000[-3:BRT]", "-1.005", "Compra no debito - Centavo", "55555555-5555-4555-8555-555555555555").Build());

        var r = await ConfirmAsync(c, batchId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("unsupported_amount_precision", await ErrorOf(r));
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(0, await db.Checkpoints.CountAsync());
        Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.SingleAsync()).State);
    }

    [Fact]
    public async Task Confirm_without_antiforgery_is_rejected()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var r = await c.Raw.PostAsync($"/api/imports/{batchId}/confirm",
            System.Net.Http.Json.JsonContent.Create(new { acknowledgeBlockingWarnings = false }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.PostAsync($"/api/imports/{batchId}/undo", null)).StatusCode);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.SingleAsync()).State);
    }
}
