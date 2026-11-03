using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Real races: 8 confirms fired at once through HTTP against the real Postgres.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportConcurrencyTests(PostgresFixture postgres)
{
    private const int Parallel = 8;

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static (string, object)[] Confirms(IEnumerable<Guid> batchIds) =>
        batchIds.Select(id => ($"/api/imports/{id}/confirm", (object)new { acknowledgeBlockingWarnings = false })).ToArray();

    private static async Task AssertNoDuplicateKeysAsync(AppDbContext db)
    {
        var duplicated = await db.Transactions
            .GroupBy(t => new { t.AccountId, t.SourceKey })
            .Where(g => g.Count() > 1).CountAsync();
        Assert.Equal(0, duplicated);
    }

    [Fact]
    public async Task Two_simultaneous_confirms_of_same_batch_create_lines_once()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc, ThreeLines().Build());

        var responses = await RaceAsync(c, Confirms(Enumerable.Repeat(batchId, Parallel)));

        var ok = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, (await JsonOf(ok)).GetProperty("created").GetInt32());
        foreach (var r in responses.Where(r => r != ok))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("already_confirmed", await ErrorOf(r));
        }

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
        Assert.Equal(1, await db.Checkpoints.CountAsync());
        Assert.Equal(ImportBatchState.Confirmed, (await db.ImportBatches.SingleAsync()).State);
        await AssertNoDuplicateKeysAsync(db);
    }

    [Fact]
    public async Task Same_file_in_two_previews_confirmed_simultaneously_creates_lines_once()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var first = await PreviewAsync(c, acc, ThreeLines().Build());

        // Same file, several open previews (e.g. two tabs uploading at once): clone the stored preview.
        var ids = new List<Guid> { first };
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var template = await db.ImportBatches.AsNoTracking().SingleAsync();
            for (var i = 1; i < Parallel; i++)
            {
                var clone = new ImportBatch
                {
                    Id = Guid.NewGuid(), AccountId = template.AccountId, Source = template.Source,
                    FileHash = template.FileHash, PeriodStart = template.PeriodStart, PeriodEnd = template.PeriodEnd,
                    State = ImportBatchState.Preview, CreatedAt = template.CreatedAt, ExpiresAt = template.ExpiresAt,
                    PreviewJson = template.PreviewJson, WarningsJson = template.WarningsJson,
                    NewCount = template.NewCount, ExistingCount = template.ExistingCount,
                };
                db.ImportBatches.Add(clone);
                ids.Add(clone.Id);
            }

            await db.SaveChangesAsync();
        }

        var responses = await RaceAsync(c, Confirms(ids));

        var ok = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, (await JsonOf(ok)).GetProperty("created").GetInt32());
        foreach (var r in responses.Where(r => r != ok))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("already_imported", await ErrorOf(r));
        }

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(3, await db.Transactions.CountAsync());
            Assert.Equal(1, await db.Checkpoints.CountAsync());
            Assert.Equal(1, await db.ImportBatches.CountAsync(b => b.State == ImportBatchState.Confirmed));
            await AssertNoDuplicateKeysAsync(db);
        }
    }

    [Fact]
    public async Task Overlapping_files_confirmed_simultaneously_create_each_line_once()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        string[] shared = [Fit1, Fit2, Fit3, "77777777-7777-4777-8777-777777777777"];

        // Different files (different hashes) sharing 4 FITIDs in varying order, plus one line of their own each.
        var ids = new List<Guid>();
        for (var i = 0; i < Parallel; i++)
        {
            var file = OfxTestFile.Checking();
            for (var k = 0; k < shared.Length; k++)
            {
                var key = shared[(k + i) % shared.Length];
                file.Line($"202610{10 + Array.IndexOf(shared, key)}000000[-3:BRT]", "-1.00", "Compra no debito - Loja", key);
            }

            file.Line("20261025000000[-3:BRT]", "-2.00", "Compra no debito - Outra", $"99999999-0000-4000-8000-00000000000{i}");
            ids.Add(await PreviewAsync(c, acc, file.Build()));
        }

        var responses = await RaceAsync(c, Confirms(ids));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var created = 0;
        var skipped = 0;
        foreach (var r in responses)
        {
            var body = await JsonOf(r);
            created += body.GetProperty("created").GetInt32();
            skipped += body.GetProperty("skipped").GetInt32();
        }

        Assert.Equal(shared.Length + Parallel, created);
        Assert.Equal(shared.Length * (Parallel - 1), skipped);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(shared.Length + Parallel, await db.Transactions.CountAsync());
        Assert.Equal(1, await db.Checkpoints.CountAsync()); // same LEDGERBAL in every file
        Assert.Equal(Parallel, await db.ImportBatches.CountAsync(b => b.State == ImportBatchState.Confirmed));
        await AssertNoDuplicateKeysAsync(db);
    }
}
