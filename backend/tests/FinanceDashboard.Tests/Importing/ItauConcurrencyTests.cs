using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Itaú confirms fired at once through HTTP against the real Postgres (content key + ordinal).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ItauConcurrencyTests(PostgresFixture postgres)
{
    private const string Day = "20261007000000[-3:BRT]";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static (string, object)[] Confirms(IEnumerable<Guid> batchIds) =>
        batchIds.Select(id => ($"/api/imports/{id}/confirm", (object)new { acknowledgeBlockingWarnings = false })).ToArray();

    [Fact]
    public async Task Concurrent_confirms_of_overlapping_files_never_duplicate()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        // Two different exports: the same group of 4 identical fees plus lines of their own (2 and 3).
        var a = OfxTestFile.Itau().NoLedger();
        var b = OfxTestFile.Itau().NoLedger();
        foreach (var file in new[] { a, b })
            for (var i = 0; i < 4; i++) file.Line(Day, "-2.50", "TARIFA FICTICIA");
        a.Line("20261003000000", "-11.00", "PIX QRS LOJA A1 FICTICIA").Line("20261004000000", "-12.00", "PIX QRS LOJA A2 FICTICIA");
        b.Line("20261020000000", "-21.00", "PIX QRS LOJA B1 FICTICIA").Line("20261021000000", "-22.00", "PIX QRS LOJA B2 FICTICIA")
            .Line("20261022000000", "-23.00", "PIX QRS LOJA B3 FICTICIA");

        // Both previews are taken before either confirm: each believes the 4 fees are new.
        var previewA = await PreviewAsync(c, acc, a.Build());
        var previewB = await PreviewAsync(c, acc, b.Build());

        var responses = await RaceAsync(c, Confirms([previewA, previewB]));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var created = 0;
        var skipped = 0;
        foreach (var r in responses)
        {
            var body = await JsonOf(r);
            created += body.GetProperty("created").GetInt32();
            skipped += body.GetProperty("skipped").GetInt32();
        }

        Assert.Equal(4 + 2 + 3, created);
        Assert.Equal(4, skipped); // the loser skipped exactly the fees the winner wrote

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(9, await db.Transactions.CountAsync());
        var fees = await db.Transactions.Where(t => t.OriginalDescription == "TARIFA FICTICIA")
            .OrderBy(t => t.Ordinal).Select(t => t.Ordinal).ToListAsync();
        Assert.Equal([0, 1, 2, 3], fees);
        Assert.Equal(2, await db.ImportBatches.CountAsync(x => x.State == ImportBatchState.Confirmed));
    }

    [Fact]
    public async Task Concurrent_confirm_same_invoice_batch_twice()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var card = await CreateAccountAsync(c, "CreditCard");
        var batch = await PreviewAsync(c, card, XlsxTestFile.Invoice("2026-11-10", total: "60.00")
            .Line("2026-10-03", "LOJA FICTICIA", "20.00").Line("2026-10-03", "LOJA FICTICIA", "20.00")
            .Line("2026-10-04", "MERCADO FICTICIO", "20.00")
            .Build(), "fatura.xlsx");

        var responses = await RaceAsync(c, Confirms([batch, batch]));

        var ok = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, (await JsonOf(ok)).GetProperty("created").GetInt32());
        var refused = Assert.Single(responses, r => r != ok);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("already_confirmed", await ErrorOf(refused));

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
        Assert.Equal(ImportBatchState.Confirmed, (await db.ImportBatches.SingleAsync()).State);
    }
}
