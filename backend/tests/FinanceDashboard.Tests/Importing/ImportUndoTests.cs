using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

[Collection(PostgresCollection.Name)]
public sealed class ImportUndoTests(PostgresFixture postgres)
{
    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static async Task<Guid> ImportAsync(AuthedClient c, Guid acc, byte[] file)
    {
        var batchId = await PreviewAsync(c, acc, file);
        await ConfirmOkAsync(c, batchId);
        return batchId;
    }

    [Fact]
    public async Task Undo_removes_lines_checkpoint_and_frees_hash_allowing_reimport()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsync("/api/transactions",
            new { accountId = acc, date = "2026-10-02", amount = "-5.00", description = "Manual" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsync($"/api/accounts/{acc}/checkpoints",
            new { date = "2026-09-30", amount = "10.00" })).StatusCode);
        var bytes = ThreeLines().Build();
        var batchId = await ImportAsync(c, acc, bytes);

        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batchId)).StatusCode);

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var left = await db.Transactions.ToListAsync();
            Assert.Equal(TransactionOrigin.Manual, Assert.Single(left).Origin);
            Assert.Equal(CheckpointOrigin.Manual, (await db.Checkpoints.SingleAsync()).Origin);
            Assert.Equal(ImportBatchState.Undone, (await db.ImportBatches.SingleAsync()).State);
        }

        Assert.Equal("Undone", (await JsonOf(await c.GetAsync($"/api/imports/{batchId}"))).GetProperty("state").GetString());

        var reupload = await UploadAsync(c, acc, bytes);
        var preview = await JsonOf(reupload);
        Assert.Equal(3, preview.GetProperty("newCount").GetInt32());
        Assert.DoesNotContain(preview.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "AlreadyImported");
        var again = await ConfirmOkAsync(c, preview.GetProperty("batchId").GetGuid());
        Assert.Equal(3, again.GetProperty("created").GetInt32());

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(4, await db.Transactions.CountAsync());
            Assert.Equal(2, await db.Checkpoints.CountAsync());
        }
    }

    [Fact]
    public async Task Undo_removes_soft_deleted_lines_of_the_batch()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await ImportAsync(c, acc, ThreeLines().Build());
        Guid hidden;
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            hidden = await db.Transactions.Where(t => t.SourceKey == Fit2).Select(t => t.Id).SingleAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/transactions/{hidden}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batchId)).StatusCode);

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(0, await db.Transactions.CountAsync());
            Assert.Equal(0, await db.Checkpoints.CountAsync());
        }

        // Undo removed the hidden line for real, so a re-import brings it back.
        var reimport = await ImportAsync(c, acc, ThreeLines().Build());
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(3, await db.Transactions.CountAsync(t => t.BatchId == reimport && !t.IsDeleted));
        }
    }

    [Fact]
    public async Task Undo_reports_edited_count_beforehand()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await ImportAsync(c, acc, ThreeLines().Build());
        Assert.Equal(0, (await JsonOf(await c.GetAsync($"/api/imports/{batchId}"))).GetProperty("editedCount").GetInt32());

        Guid edited;
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            edited = await db.Transactions.Where(t => t.SourceKey == Fit3).Select(t => t.Id).SingleAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/transactions/{edited}",
            new { date = "2026-10-20", amount = "-40.50", description = "Fatura paga", state = "Normal" })).StatusCode);

        var detail = await JsonOf(await c.GetAsync($"/api/imports/{batchId}"));
        Assert.Equal(1, detail.GetProperty("editedCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batchId)).StatusCode);
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(0, await db.Transactions.CountAsync()); // edited lines go too; the client warned first
        }
    }

    [Fact]
    public async Task Other_users_batch_undo_is_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var accA = await CreateAccountAsync(a);
        var confirmed = await ImportAsync(a, accA, ThreeLines().Build());
        var preview = await PreviewAsync(a, accA, OfxTestFile.Checking()
            .Line("20261105000000[-3:BRT]", "-1.00", "Outra", "66666666-6666-4666-8666-666666666666").Build());

        Assert.Equal(HttpStatusCode.NotFound, (await UndoAsync(b, confirmed)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UndoAsync(b, preview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UndoAsync(b, Guid.NewGuid())).StatusCode);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, a.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
        Assert.Equal(1, await db.Checkpoints.CountAsync());
        Assert.Equal(ImportBatchState.Confirmed, (await db.ImportBatches.SingleAsync(x => x.Id == confirmed)).State);
        Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.SingleAsync(x => x.Id == preview)).State);
    }

    [Fact]
    public async Task Undo_of_preview_removes_the_batch_and_repeated_undo_is_409()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var preview = await PreviewAsync(c, acc, ThreeLines().Build());

        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, preview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/imports/{preview}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UndoAsync(c, preview)).StatusCode);

        var confirmed = await ImportAsync(c, acc, ThreeLines().Build());
        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, confirmed)).StatusCode);
        var twice = await UndoAsync(c, confirmed);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("already_undone", await ErrorOf(twice));
        var confirmUndone = await ConfirmAsync(c, confirmed);
        Assert.Equal(HttpStatusCode.Conflict, confirmUndone.StatusCode);
        Assert.Equal("already_undone", await ErrorOf(confirmUndone));
    }
}
