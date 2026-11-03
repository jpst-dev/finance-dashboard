using System.Net;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Itaú invoice (xlsx) through the HTTP API on Postgres: preview, checks against the database, confirm (synthetic files only).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ItauInvoiceImportTests(PostgresFixture postgres)
{
    private const string Xlsx = "fatura.xlsx";
    private const string Due = "2026-11-10";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    /// <summary>Lines A (100), B (30, optional) and C (50); declared total = sum of the lines.</summary>
    private static XlsxTestFile Abc(bool withB = true, string due = Due)
    {
        var f = XlsxTestFile.Invoice(due, total: withB ? "180.00" : "150.00")
            .Line("2026-10-03", "LOJA A FICTICIA", "100.00");
        if (withB) f.Line("2026-10-04", "LOJA B FICTICIA", "30.00");
        return f.Line("2026-10-05", "LOJA C FICTICIA", "50.00");
    }

    private static IEnumerable<JsonElement> Warnings(JsonElement preview) => preview.GetProperty("warnings").EnumerateArray();

    private static bool Has(JsonElement preview, string code) => Warnings(preview).Any(w => w.GetProperty("code").GetString() == code);

    private static async Task<(AuthedClient Client, Guid Card)> CardAsync(ApiFactory f)
    {
        var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        return (c, await CreateAccountAsync(c, "CreditCard"));
    }

    private static async Task<JsonElement> PreviewOkAsync(AuthedClient c, Guid account, XlsxTestFile file)
    {
        var r = await UploadAsync(c, account, file.Build(), Xlsx);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    private static Guid BatchOf(JsonElement preview) => preview.GetProperty("batchId").GetGuid();

    private static async Task<Guid> LineIdAsync(ApiFactory f, AuthedClient c, string description)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        return await db.Transactions.Where(t => t.OriginalDescription == description).Select(t => t.Id).SingleAsync();
    }

    [Fact]
    public async Task Xlsx_upload_previews_invoice_with_metadata()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        var body = await PreviewOkAsync(c, card, XlsxTestFile.Invoice(Due, total: "120.00")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00", "Parcela 2 de 10", "Físico", "****1234"));

        Assert.Equal("ItauCard", body.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("periodStart").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("periodEnd").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("checkpoint").ValueKind);
        var invoice = body.GetProperty("invoice");
        Assert.Equal(Due, invoice.GetProperty("dueDate").GetString());
        Assert.Equal(JsonValueKind.String, invoice.GetProperty("declaredTotal").ValueKind);
        Assert.Equal(120m, decimal.Parse(invoice.GetProperty("declaredTotal").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(Warnings(body));

        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(0, line.GetProperty("ordinal").GetInt32());
        Assert.Equal(2, line.GetProperty("installmentNumber").GetInt32());
        Assert.Equal(10, line.GetProperty("installmentTotal").GetInt32());
        Assert.Equal("1234", line.GetProperty("cardSuffix").GetString());
        Assert.Equal("Physical", line.GetProperty("cardKind").GetString());
        Assert.Equal("2026-10-03", line.GetProperty("purchaseDate").GetString());
        Assert.Equal("2026-10-10", line.GetProperty("date").GetString()); // installment > 1: due date − 1 month
        Assert.Equal(64, line.GetProperty("sourceKey").GetString()!.Length);

        // The list shows the invoice from the batch columns.
        var list = await JsonOf(await c.GetAsync("/api/imports"));
        var summary = Assert.Single(list.EnumerateArray());
        Assert.Equal(Due, summary.GetProperty("invoice").GetProperty("dueDate").GetString());

        // Confirmed: the line keeps its metadata.
        await ConfirmOkAsync(c, BatchOf(body));
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var t = await db.Transactions.SingleAsync();
        Assert.Equal(-120m, t.Amount);
        Assert.Equal(new DateOnly(2026, 11, 10), t.StatementDate);
        Assert.Equal(new DateOnly(2026, 10, 3), t.PurchaseDate);
        Assert.Equal("1234", t.CardSuffix);
        Assert.Equal(CardKind.Physical, t.CardKind);
        var after = await JsonOf(await c.GetAsync($"/api/imports/{BatchOf(body)}"));
        Assert.Equal(Due, after.GetProperty("invoice").GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task Xlsx_into_checking_account_is_account_type_mismatch()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await CreateAccountAsync(c);
        var r = await UploadAsync(c, checking, Abc().Build(), Xlsx);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("account_type_mismatch", await ErrorOf(r));
    }

    [Theory]
    [InlineData("EncryptedCfb", "encrypted_xlsx")]
    [InlineData("LegacyXls", "not_xlsx")]
    [InlineData("ZipBomb", "xlsx_limits_exceeded")]
    [InlineData("Macro", "xlsx_unsafe_content")]
    [InlineData("UnknownHeader", "unsupported_layout")]
    [InlineData("FormulaInLine", "invalid_cell")]
    [InlineData("Bank0001", "unsupported_bank")]
    public async Task New_error_codes_reach_client(string file, string error)
    {
        var content = file switch
        {
            "EncryptedCfb" => XlsxTestFile.EncryptedCfb(),
            "LegacyXls" => XlsxTestFile.LegacyXls(),
            "ZipBomb" => XlsxTestFile.LyingSizeBomb(), // small on the wire (a real bomb above 5 MB is 413 first)
            "Macro" => Abc().Entry("xl/vbaProject.bin", [1, 2, 3]).Build(),
            "UnknownHeader" => Abc().Header([.. XlsxTestFile.DefaultHeader, "Coluna Ficticia"]).Build(),
            "FormulaInLine" => Abc().FormulaInLine(1).Build(),
            "Bank0001" => OfxTestFile.Checking().Bank("0001").Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };

        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;
        var r = await UploadAsync(c, card, content, file == "Bank0001" ? "extrato.ofx" : Xlsx);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(error, await ErrorOf(r));
    }

    [Fact]
    public async Task Xlsx_above_5_mb_is_too_large()
    {
        var big = XlsxTestFile.ZipBomb(60L * 1024 * 1024);
        Assert.True(big.Length > 5 * 1024 * 1024);

        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;
        var r = await UploadAsync(c, card, big, Xlsx);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Equal("too_large", await ErrorOf(r));
    }

    [Fact]
    public async Task Total_mismatch_blocks_until_acknowledged()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        var body = await PreviewOkAsync(c, card, XlsxTestFile.Invoice(Due, total: "999.00").Line("2026-10-03", "LOJA FICTICIA", "100.00"));
        var w = Assert.Single(Warnings(body), x => x.GetProperty("code").GetString() == "InvoiceTotalMismatch");
        Assert.Equal("blocking", w.GetProperty("severity").GetString());
        Assert.True(w.GetProperty("overridable").GetBoolean());

        var refused = await ConfirmAsync(c, BatchOf(body));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var refusal = await JsonOf(refused);
        Assert.Equal("blocking_warnings", refusal.GetProperty("error").GetString());
        Assert.Contains(refusal.GetProperty("warnings").EnumerateArray(), x => x.GetProperty("code").GetString() == "InvoiceTotalMismatch");

        Assert.Equal(1, (await ConfirmOkAsync(c, BatchOf(body), acknowledge: true)).GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task Missing_lines_block_without_override()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        var v1 = BatchOf(await PreviewOkAsync(c, card, Abc()));
        Assert.Equal(3, (await ConfirmOkAsync(c, v1)).GetProperty("created").GetInt32());

        var v2 = await PreviewOkAsync(c, card, Abc(withB: false));
        var w = Assert.Single(Warnings(v2), x => x.GetProperty("code").GetString() == "InvoiceHasMissingLines");
        Assert.Equal("blocking", w.GetProperty("severity").GetString());
        Assert.False(w.GetProperty("overridable").GetBoolean());
        Assert.Contains("1", w.GetProperty("message").GetString());

        // Acknowledging does not help: the warning is not overridable.
        var refused = await ConfirmAsync(c, BatchOf(v2), acknowledge: true);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var refusal = await JsonOf(refused);
        Assert.Equal("blocking_warnings", refusal.GetProperty("error").GetString());
        var rw = Assert.Single(refusal.GetProperty("warnings").EnumerateArray(),
            x => x.GetProperty("code").GetString() == "InvoiceHasMissingLines");
        Assert.False(rw.GetProperty("overridable").GetBoolean());

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(3, await db.Transactions.CountAsync());
            Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.SingleAsync(b => b.Id == BatchOf(v2))).State);
        }

        // Undoing the previous batch of this invoice clears the way.
        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, v1)).StatusCode);
        var again = await PreviewOkAsync(c, card, Abc(withB: false));
        Assert.False(Has(again, "InvoiceHasMissingLines"));
        Assert.Equal(2, (await ConfirmOkAsync(c, BatchOf(again))).GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task Soft_deleted_line_is_not_missing()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        await ConfirmOkAsync(c, BatchOf(await PreviewOkAsync(c, card, Abc())));
        var b = await LineIdAsync(f, c, "LOJA B FICTICIA");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/transactions/{b}")).StatusCode);

        var v2 = await PreviewOkAsync(c, card, Abc(withB: false));
        Assert.False(Has(v2, "InvoiceHasMissingLines"));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(c, BatchOf(v2))).StatusCode);
    }

    [Fact]
    public async Task Edited_line_counts_as_missing()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        await ConfirmOkAsync(c, BatchOf(await PreviewOkAsync(c, card, Abc())));
        var b = await LineIdAsync(f, c, "LOJA B FICTICIA");
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/transactions/{b}",
            new { date = "2026-10-04", amount = "-31.00", description = "Loja B editada", state = "Normal" })).StatusCode);

        var v2 = await PreviewOkAsync(c, card, Abc(withB: false));
        Assert.True(Has(v2, "InvoiceHasMissingLines"));
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(c, BatchOf(v2), acknowledge: true)).StatusCode);
    }

    [Fact]
    public async Task Missing_lines_detected_at_confirm_time()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        var v1 = BatchOf(await PreviewOkAsync(c, card, Abc()));
        var v2 = await PreviewOkAsync(c, card, Abc(withB: false));
        Assert.False(Has(v2, "InvoiceHasMissingLines")); // nothing confirmed yet
        await ConfirmOkAsync(c, v1);

        var refused = await ConfirmAsync(c, BatchOf(v2), acknowledge: true);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains((await JsonOf(refused)).GetProperty("warnings").EnumerateArray(),
            x => x.GetProperty("code").GetString() == "InvoiceHasMissingLines");
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Unknown_card_suffix_warns_only_after_first_import()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        var first = await PreviewOkAsync(c, card, XlsxTestFile.Invoice(Due, total: "10.00")
            .Line("2026-10-03", "LOJA FICTICIA", "10.00", cardNumber: "****1234"));
        Assert.False(Has(first, "UnknownCardSuffix"));
        await ConfirmOkAsync(c, BatchOf(first));

        var second = await PreviewOkAsync(c, card, XlsxTestFile.Invoice("2026-12-10", total: "30.00")
            .Line("2026-11-03", "OUTRA LOJA FICTICIA", "10.00", cardNumber: "****1234")
            .Line("2026-11-04", "MAIS UMA LOJA FICTICIA", "20.00", cardNumber: "****5678"));
        var w = Assert.Single(Warnings(second), x => x.GetProperty("code").GetString() == "UnknownCardSuffix");
        Assert.Equal("warning", w.GetProperty("severity").GetString());
        Assert.Contains("1 line", w.GetProperty("message").GetString());
        Assert.DoesNotContain("5678", w.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(c, BatchOf(second))).StatusCode);
    }

    [Fact]
    public async Task Superset_invoice_inserts_only_new_lines()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        await ConfirmOkAsync(c, BatchOf(await PreviewOkAsync(c, card, Abc(withB: false))));
        var v2 = await PreviewOkAsync(c, card, Abc());
        Assert.Equal(1, v2.GetProperty("newCount").GetInt32());
        Assert.Equal(2, v2.GetProperty("existingCount").GetInt32());
        Assert.False(Has(v2, "InvoiceHasMissingLines"));

        var result = await ConfirmOkAsync(c, BatchOf(v2));
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(2, result.GetProperty("skipped").GetInt32());
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Identical_invoice_reexported_is_noop()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        var a = Abc().ZipTimestamp(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero)).Build();
        var b = Abc().ZipTimestamp(new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero)).Build();
        Assert.NotEqual(a, b); // another export of the same invoice: different file hash

        await ConfirmOkAsync(c, await PreviewAsync(c, card, a, Xlsx));
        var r = await UploadAsync(c, card, b, Xlsx);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var v2 = await JsonOf(r);
        Assert.Equal(0, v2.GetProperty("newCount").GetInt32());
        Assert.Equal(3, v2.GetProperty("existingCount").GetInt32());
        Assert.All(v2.GetProperty("lines").EnumerateArray(), l => Assert.True(l.GetProperty("existing").GetBoolean()));
        Assert.False(Has(v2, "InvoiceHasMissingLines"));
        Assert.False(Has(v2, "AlreadyImported"));

        Assert.Equal(0, (await ConfirmOkAsync(c, BatchOf(v2))).GetProperty("created").GetInt32());
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(3, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Overrides_with_ordinal_on_invoice()
    {
        await using var f = await NewFactoryAsync();
        var (c, card) = await CardAsync(f);
        using var _ = c;

        // Two identical payment lines: same key, ordinals 0 and 1, both Pendente.
        var body = await PreviewOkAsync(c, card, XlsxTestFile.Invoice(Due, total: "100.00")
            .Line("2026-10-03", "LOJA FICTICIA", "100.00")
            .Line("2026-10-10", "PAGAMENTO EFETUADO", "-50.00")
            .Line("2026-10-10", "PAGAMENTO EFETUADO", "-50.00"));
        Assert.Empty(Warnings(body));
        var payments = body.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("description").GetString() == "PAGAMENTO EFETUADO").ToList();
        Assert.Equal([0, 1], payments.Select(p => p.GetProperty("ordinal").GetInt32()));
        Assert.All(payments, p => Assert.Equal("PendingClassification", p.GetProperty("state").GetString()));
        var key = payments[0].GetProperty("sourceKey").GetString();
        Assert.Equal(key, payments[1].GetProperty("sourceKey").GetString());

        var r = await ConfirmWithAsync(c, BatchOf(body), false, new { sourceKey = key, ordinal = 1, state = "Normal" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(3, (await JsonOf(r)).GetProperty("created").GetInt32());

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var stored = await db.Transactions.Where(t => t.SourceKey == key).OrderBy(t => t.Ordinal).ToListAsync();
        Assert.Equal(TransactionState.PendingClassification, stored[0].State);
        Assert.Equal(TransactionState.Normal, stored[1].State);
    }
}
