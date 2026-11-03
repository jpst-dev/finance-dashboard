using System.Globalization;
using System.Net;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>
/// End to end on Postgres through HTTP, Itaú (content key + ordinal): re-imports, overlapping exports, ordinal holes
/// after an undo and the monthly summary. Synthetic files only. A "re-export" is a new build of the same lines: the
/// OFX gets fresh FITIDs (the Itaú key ignores them), the xlsx another zip timestamp, so the file hash differs.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ItauIdempotencyTests(PostgresFixture postgres)
{
    private const string Xlsx = "fatura.xlsx";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static string Day(int day) => $"202610{day:00}000000[-3:BRT]";

    private static OfxTestFile ItauOfx(int fromDay = 1, int toDay = 31) =>
        OfxTestFile.Itau().NoLedger().Period(Day(fromDay), Day(toDay));

    private static async Task<JsonElement> PreviewBodyAsync(AuthedClient c, Guid account, byte[] file, string name = "extrato.ofx")
    {
        var r = await UploadAsync(c, account, file, name);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    private static Guid BatchOf(JsonElement preview) => preview.GetProperty("batchId").GetGuid();

    private static int Created(JsonElement confirm) => confirm.GetProperty("created").GetInt32();

    private static IEnumerable<JsonElement> Warnings(JsonElement preview) => preview.GetProperty("warnings").EnumerateArray();

    /// <summary>Every stored line of the user, as comparable text (no ids, no batch), sorted.</summary>
    private static async Task<List<string>> SnapshotAsync(ApiFactory f, AuthedClient c)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var rows = await db.Transactions.AsNoTracking().ToListAsync();
        return rows
            .Select(t => string.Join('|', t.AccountId, t.SourceKey, t.Ordinal, t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.Amount.ToString("0.00", CultureInfo.InvariantCulture), t.OriginalDescription, t.State, t.CategoryId,
                t.Origin, t.IsDeleted, t.EditedByUser))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task Full_flow_preview_confirm_with_overrides_undo_reimport()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var file = ItauOfx()
            .Line(Day(10), "100.00", "PIX TRANSF FULANO FICTICIO")
            .Line(Day(10), "100.00", "PIX TRANSF FULANO FICTICIO")
            .Line(Day(11), "-12.30", "PIX QRS PADARIA FICTICIA")
            .Line(Day(12), "-500.00", "FATURA PAGA ITAU FICTICIO")
            .Build();

        var preview = await PreviewBodyAsync(c, acc, file);
        Assert.Equal(4, preview.GetProperty("newCount").GetInt32());
        var transfers = preview.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("description").GetString() == "PIX TRANSF FULANO FICTICIO").ToList();
        Assert.Equal([0, 1], transfers.Select(l => l.GetProperty("ordinal").GetInt32()));
        Assert.All(transfers, l => Assert.Equal("Normal", l.GetProperty("state").GetString()));
        var key = transfers[0].GetProperty("sourceKey").GetString()!;
        Assert.Equal(key, transfers[1].GetProperty("sourceKey").GetString());

        // Override only ordinal 1: the twin at ordinal 0 must keep the reader's state.
        var first = await ConfirmWithAsync(c, BatchOf(preview), false, new { sourceKey = key, ordinal = 1, state = "PendingClassification" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(4, Created(await JsonOf(first)));
        var before = await SnapshotAsync(f, c);
        Assert.Equal(4, before.Count);
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var twins = await db.Transactions.Where(t => t.SourceKey == key).OrderBy(t => t.Ordinal).ToListAsync();
            Assert.Equal([0, 1], twins.Select(t => t.Ordinal));
            Assert.Equal([TransactionState.Normal, TransactionState.PendingClassification], twins.Select(t => t.State));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, BatchOf(preview))).StatusCode);
        Assert.Empty(await SnapshotAsync(f, c));

        // Same bytes again: the undo freed the file hash, so everything is new and confirms with the same override.
        var again = await PreviewBodyAsync(c, acc, file);
        Assert.Equal(4, again.GetProperty("newCount").GetInt32());
        Assert.DoesNotContain(Warnings(again), w => w.GetProperty("code").GetString() == "AlreadyImported");
        Assert.All(again.GetProperty("lines").EnumerateArray(), l => Assert.False(l.GetProperty("existing").GetBoolean()));
        var second = await ConfirmWithAsync(c, BatchOf(again), false, new { sourceKey = key, ordinal = 1, state = "PendingClassification" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(4, Created(await JsonOf(second)));

        Assert.Equal(before, await SnapshotAsync(f, c));
    }

    [Theory]
    [InlineData("ofx")]
    [InlineData("xlsx")]
    public async Task Reimport_n_times_is_noop(string format)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c, format == "ofx" ? "Checking" : "CreditCard");

        // n = 5 lines, two pairs of identical lines among them.
        byte[] Export(int i) => format == "ofx"
            ? ItauOfx()
                .Line(Day(3), "-5.00", "TARIFA FICTICIA").Line(Day(3), "-5.00", "TARIFA FICTICIA")
                .Line(Day(4), "-12.30", "PIX QRS PADARIA FICTICIA")
                .Line(Day(9), "50.00", "PIX TRANSF FULANO FICTICIO").Line(Day(9), "50.00", "PIX TRANSF FULANO FICTICIO")
                .Build()
            : XlsxTestFile.Invoice("2026-11-10", total: "75.00")
                .Line("2026-10-03", "LOJA FICTICIA", "10.00").Line("2026-10-03", "LOJA FICTICIA", "10.00")
                .Line("2026-10-04", "MERCADO FICTICIO", "55.00")
                .Line("2026-10-08", "PAGAMENTO EFETUADO", "-40.00").Line("2026-10-08", "PAGAMENTO EFETUADO", "-40.00")
                .ZipTimestamp(new DateTimeOffset(2026, 11, 1 + i, 8, 0, 0, TimeSpan.Zero))
                .Build();
        var name = format == "ofx" ? "extrato.ofx" : Xlsx;

        var exports = Enumerable.Range(0, 3).Select(Export).ToList();
        Assert.Equal(3, exports.Select(Convert.ToBase64String).Distinct().Count()); // three different file hashes

        var created = new List<int>();
        var newCounts = new List<int>();
        foreach (var export in exports)
        {
            var preview = await PreviewBodyAsync(c, acc, export, name);
            newCounts.Add(preview.GetProperty("newCount").GetInt32());
            created.Add(Created(await ConfirmOkAsync(c, BatchOf(preview))));
        }

        Assert.Equal([5, 0, 0], newCounts);
        Assert.Equal([5, 0, 0], created);
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(5, await db.Transactions.CountAsync());
        Assert.Equal(3, await db.ImportBatches.CountAsync(b => b.State == ImportBatchState.Confirmed));
    }

    [Fact]
    public async Task Overlapping_exports_insert_only_missing()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        // One distinct line per day plus the same group of 3 identical fees on day 7 in both exports.
        byte[] Export(int from, int to)
        {
            var file = ItauOfx(from, to);
            for (var d = from; d <= to; d++)
            {
                file.Line(Day(d), $"-{d}.00", $"PIX QRS LOJA FICTICIA {d}");
                if (d == 7) for (var k = 0; k < 3; k++) file.Line(Day(7), "-2.50", "TARIFA FICTICIA");
            }

            return file.Build();
        }

        var a = await PreviewBodyAsync(c, acc, Export(1, 10));
        Assert.Equal(13, Created(await ConfirmOkAsync(c, BatchOf(a))));

        var b = await PreviewBodyAsync(c, acc, Export(5, 15));
        Assert.Equal(5, b.GetProperty("newCount").GetInt32()); // days 11..15
        Assert.Equal(9, b.GetProperty("existingCount").GetInt32()); // days 5..10 + the 3 fees
        var fees = b.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("description").GetString() == "TARIFA FICTICIA").ToList();
        Assert.Equal([0, 1, 2], fees.Select(l => l.GetProperty("ordinal").GetInt32()));
        Assert.All(fees, l => Assert.True(l.GetProperty("existing").GetBoolean()));

        var confirmed = await ConfirmOkAsync(c, BatchOf(b));
        Assert.Equal(5, Created(confirmed));
        Assert.Equal(9, confirmed.GetProperty("skipped").GetInt32());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(15 + 3, await db.Transactions.CountAsync());
        var group = await db.Transactions.Where(t => t.OriginalDescription == "TARIFA FICTICIA")
            .OrderBy(t => t.Ordinal).Select(t => t.Ordinal).ToListAsync();
        Assert.Equal([0, 1, 2], group);
        var days = await db.Transactions.Where(t => t.OriginalDescription != "TARIFA FICTICIA")
            .Select(t => t.Date.Day).OrderBy(d => d).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 15), days);
    }

    [Fact]
    public async Task Multiset_with_ordinal_holes_after_undo()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        static byte[] Identical(int count)
        {
            var file = ItauOfx();
            for (var i = 0; i < count; i++) file.Line(Day(7), "-2.50", "TARIFA FICTICIA");
            return file.Build();
        }

        var a = BatchOf(await PreviewBodyAsync(c, acc, Identical(2)));
        Assert.Equal(2, Created(await ConfirmOkAsync(c, a)));
        var b = await PreviewBodyAsync(c, acc, Identical(4));
        Assert.Equal([true, true, false, false], b.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("existing").GetBoolean()));
        Assert.Equal(2, Created(await ConfirmOkAsync(c, BatchOf(b))));

        // Undoing A leaves B's ordinals 2 and 3: a hole at 0 and 1.
        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, a)).StatusCode);
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal([2, 3], await db.Transactions.OrderBy(t => t.Ordinal).Select(t => t.Ordinal).ToListAsync());
        }

        // Another export of B: the preview reports which pairs exist, not how many lines, so the hole is new.
        var again = await PreviewBodyAsync(c, acc, Identical(4));
        var lines = again.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal([0, 1, 2, 3], lines.Select(l => l.GetProperty("ordinal").GetInt32()));
        Assert.Equal([false, false, true, true], lines.Select(l => l.GetProperty("existing").GetBoolean()));
        Assert.Equal(2, again.GetProperty("newCount").GetInt32());

        var confirmed = await ConfirmOkAsync(c, BatchOf(again));
        Assert.Equal(2, Created(confirmed));
        Assert.Equal(2, confirmed.GetProperty("skipped").GetInt32());
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal([0, 1, 2, 3], await db.Transactions.OrderBy(t => t.Ordinal).Select(t => t.Ordinal).ToListAsync());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Property_reimports_and_overlaps_converge_to_max_multiplicity(int seed)
    {
        var rng = new Random(seed);
        const int keys = 6;
        var keyDay = Enumerable.Range(0, keys).Select(_ => rng.Next(1, 29)).ToArray();
        var baseMultiplicity = Enumerable.Range(0, keys).Select(_ => rng.Next(1, 5)).ToArray();

        // 4 files: a period cut of the month and, per key inside it, a multiplicity between 1 and the base one.
        var files = new List<int[]>();
        for (var i = 0; i < 4; i++)
        {
            var from = rng.Next(1, 15);
            var to = rng.Next(from + 5, 29);
            if (!keyDay.Any(d => d >= from && d <= to)) (from, to) = (1, 28);
            files.Add(Enumerable.Range(0, keys)
                .Select(k => keyDay[k] >= from && keyDay[k] <= to ? rng.Next(1, baseMultiplicity[k] + 1) : 0).ToArray());
        }

        // Every file at least once, plus 3 re-imports, in a random order.
        var order = Enumerable.Range(0, 4).Concat(Enumerable.Range(0, 3).Select(_ => rng.Next(4)))
            .OrderBy(_ => rng.Next()).ToList();

        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        var stored = new int[keys]; // model: max multiplicity seen so far, per key
        foreach (var index in order)
        {
            var multiplicity = files[index];
            var lines = Enumerable.Range(0, keys).SelectMany(k => Enumerable.Repeat(k, multiplicity[k]))
                .OrderBy(_ => rng.Next()).ToList();
            var file = ItauOfx();
            foreach (var k in lines) file.Line(Day(keyDay[k]), $"-{k + 1}.00", $"PIX QRS LOJA FICTICIA {k}");

            var expectedNew = Enumerable.Range(0, keys).Sum(k => Math.Max(0, multiplicity[k] - stored[k]));
            var preview = await PreviewBodyAsync(c, acc, file.Build());
            Assert.Equal(expectedNew, preview.GetProperty("newCount").GetInt32());
            Assert.Equal(expectedNew, Created(await ConfirmOkAsync(c, BatchOf(preview))));
            for (var k = 0; k < keys; k++) stored[k] = Math.Max(stored[k], multiplicity[k]);
        }

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var rows = await db.Transactions.AsNoTracking().ToListAsync();
        for (var k = 0; k < keys; k++)
        {
            var description = $"PIX QRS LOJA FICTICIA {k}";
            var ordinals = rows.Where(t => t.OriginalDescription == description).Select(t => t.Ordinal).Order().ToList();
            // Max multiplicity seen, no identical line lost (ordinals are exactly 0..max-1), none duplicated.
            Assert.Equal(Enumerable.Range(0, stored[k]), ordinals);
        }

        Assert.Equal(stored.Sum(), rows.Count);
    }

    [Fact]
    public async Task Installment_counts_in_due_minus_one_month_summary()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var card = await CreateAccountAsync(c, "CreditCard");

        // Due 2026-11-10: installment 3/10 of a March purchase is dated 2026-10-10; the payment is Pendente.
        var preview = await PreviewBodyAsync(c, card, XlsxTestFile.Invoice("2026-11-10", total: "130.00")
            .Line("2026-03-15", "LOJA PARCELADA FICTICIA", "100.00", "Parcela 3 de 10")
            .Line("2026-10-05", "MERCADO FICTICIO", "30.00")
            .Line("2026-10-12", "PAGAMENTO EFETUADO", "-500.00")
            .Build(), Xlsx);
        Assert.Empty(Warnings(preview));
        Assert.Equal(3, Created(await ConfirmOkAsync(c, BatchOf(preview))));

        var october = await JsonOf(await c.GetAsync("/api/summary?month=2026-10"));
        Assert.Equal("0.00", october.GetProperty("income").GetString());
        Assert.Equal("-130.00", october.GetProperty("expenses").GetString()); // installment + purchase, no payment
        Assert.Equal("-130.00", october.GetProperty("net").GetString());
        Assert.Equal(1, october.GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal("500.00", october.GetProperty("pending").GetProperty("total").GetString());

        var march = await JsonOf(await c.GetAsync("/api/summary?month=2026-03"));
        Assert.Equal("0.00", march.GetProperty("expenses").GetString());
        Assert.Equal(0, march.GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Empty(march.GetProperty("byCategory").EnumerateArray());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var installment = await db.Transactions.SingleAsync(t => t.InstallmentNumber == 3);
        Assert.Equal(new DateOnly(2026, 10, 10), installment.Date);
        Assert.Equal(new DateOnly(2026, 3, 15), installment.PurchaseDate);
    }

    [Fact]
    public async Task Pending_checking_lines_stay_out_of_summary()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        var preview = await PreviewBodyAsync(c, acc, ItauOfx()
            .Line(Day(5), "3000.00", "REMUNERACAO SALARIO FICTICIO")
            .Line(Day(6), "-12.30", "PIX QRS PADARIA FICTICIA")
            .Line(Day(10), "-500.00", "FATURA PAGA ITAU FICTICIO")
            .Line(Day(11), "200.00", "PIX TRANSF FULANO FICTICIO")
            .Build());
        Assert.Equal(4, Created(await ConfirmOkAsync(c, BatchOf(preview))));

        var october = await JsonOf(await c.GetAsync("/api/summary?month=2026-10"));
        Assert.Equal("3200.00", october.GetProperty("income").GetString());
        Assert.Equal("-12.30", october.GetProperty("expenses").GetString());
        Assert.Equal("3187.70", october.GetProperty("net").GetString());
        Assert.Equal(1, october.GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal("-500.00", october.GetProperty("pending").GetProperty("total").GetString());
    }
}
