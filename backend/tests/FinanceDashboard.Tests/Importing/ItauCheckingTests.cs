using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

[Collection(PostgresCollection.Name)]
public sealed class ItauCheckingTests(PostgresFixture postgres)
{
    private const string Day = "20261015000000";

    private static ParsedStatement Read(OfxTestFile f) => StatementReader.Read(f.Build());

    [Fact]
    public void Parses_unclosed_sgml_cp1252_with_accents_intact()
    {
        var s = Read(OfxTestFile.Itau().Line(Day, "-12.30", "PIX TRANSF JOÃO FICTICIO"));
        Assert.Equal("PIX TRANSF JOÃO FICTICIO", s.Lines[0].Memo);
        Assert.Equal("12345", s.AccountExternalId);
    }

    [Fact]
    public void Dates_are_literal_ignoring_timezone()
    {
        var s = Read(OfxTestFile.Itau().Line("20261031235959[-3:BRT]", "-1.00", "X"));
        Assert.Equal(new DateOnly(2026, 10, 31), s.Lines[0].Date);
    }

    [Fact]
    public void Four_identical_lines_same_day_become_four_ordinals()
    {
        var f = OfxTestFile.Itau();
        for (var i = 0; i < 4; i++) f.Line(Day, "-5.00", "TARIFA FICTICIA");
        f.Line(Day, "-5.00", "OUTRA TARIFA FICTICIA");
        var s = Read(f);

        Assert.Equal([0, 1, 2, 3, 0], s.Lines.Select(l => l.Ordinal));
        Assert.Single(s.Lines.Take(4).Select(l => l.SourceKey).Distinct());
        Assert.NotEqual(s.Lines[0].SourceKey, s.Lines[4].SourceKey);
    }

    [Fact]
    public void Key_ignores_fitid_and_checknum()
    {
        var a = Read(OfxTestFile.Itau().Line(Day, "-5.00", "Tarifa  Ficticia", "FIT-A"));
        var b = Read(OfxTestFile.Itau().Line(Day, "-5.00", "TARIFA FICTICIA", "FIT-B"));
        Assert.Equal(a.Lines[0].SourceKey, b.Lines[0].SourceKey);
        Assert.Equal(64, a.Lines[0].SourceKey.Length);
    }

    [Fact]
    public void Key_keeps_amount_sign()
    {
        var a = Read(OfxTestFile.Itau().Line(Day, "-5.00", "X"));
        var b = Read(OfxTestFile.Itau().Line(Day, "5.00", "X"));
        Assert.NotEqual(a.Lines[0].SourceKey, b.Lines[0].SourceKey);
    }

    [Theory]
    [InlineData("FATURA PAGA ITAU UNICLASS")]
    [InlineData("PIX TRANSF JOAO FICTICIO")]
    [InlineData("PIX CARTAO FICTICIO")]
    [InlineData("PAG BOLETO LOJA FICTICIA")]
    [InlineData("CREDITO LIBERAD CONTRATO")]
    [InlineData("EST CRED ALGO")]
    [InlineData("CREDITO CONSIGNADO X")]
    [InlineData("APLICACAO CDB")]
    [InlineData("RESGATE CDB")]
    [InlineData("crédito liberad contrato")]
    public void Classifies_pending_prefixes(string memo)
    {
        foreach (var amount in new[] { "-10.00", "10.00" })
        {
            var s = Read(OfxTestFile.Itau().Line(Day, amount, memo));
            Assert.Equal(TransactionState.PendingClassification, s.Lines[0].State);
            Assert.DoesNotContain(s.Warnings, w => w.Code == "UnknownLineKinds");
        }
    }

    [Theory]
    [InlineData("PIX QRS PADARIA FICTICIA")]
    [InlineData("REND PAGO APLIC AUT")]
    [InlineData("REMUNERACAO SALARIO FICTICIO")]
    public void Known_normal_prefixes_are_normal_without_unknown_count(string memo)
    {
        var s = Read(OfxTestFile.Itau().Line(Day, "-10.00", memo));
        Assert.Equal(TransactionState.Normal, s.Lines[0].State);
        Assert.DoesNotContain(s.Warnings, w => w.Code == "UnknownLineKinds");
    }

    [Fact]
    public void Unknown_lines_are_normal_and_counted()
    {
        var s = Read(OfxTestFile.Itau()
            .Line(Day, "-1.00", "COISA ESTRANHA UM").Line(Day, "-2.00", "COISA ESTRANHA DOIS").Line(Day, "-3.00", "PIX QRS X"));
        Assert.All(s.Lines, l => Assert.Equal(TransactionState.Normal, l.State));
        var w = Assert.Single(s.Warnings, x => x.Code == "UnknownLineKinds");
        Assert.Contains("2", w.Message);
        Assert.Equal(ImportWarning.Warn, w.Severity);
    }

    [Fact]
    public void Ledger_becomes_checkpoint_when_no_line_after_asof()
    {
        var s = Read(OfxTestFile.Itau().Line("20261030000000", "-1.00", "PIX QRS X").Ledger("50.00", "20261031000000"));
        Assert.Equal(new ParsedBalance(50m, new DateOnly(2026, 10, 31)), s.LedgerBalance);
        Assert.Equal(s.LedgerBalance, s.LedgerRead);
        Assert.DoesNotContain(s.Warnings, w => w.Code == "LedgerBalanceBeforeLastLine");
    }

    [Fact]
    public void Ledger_before_last_line_warns_and_has_no_checkpoint()
    {
        var s = Read(OfxTestFile.Itau().Line("20261031000000", "-1.00", "PIX QRS X").Ledger("50.00", "20261015000000"));
        Assert.Null(s.LedgerBalance);
        Assert.NotNull(s.LedgerRead);
        var w = Assert.Single(s.Warnings, x => x.Code == "LedgerBalanceBeforeLastLine");
        Assert.Equal(ImportWarning.Warn, w.Severity);
    }

    [Fact]
    public void Reader_exceptions_carry_no_content()
    {
        const string marker = "ITAUMARKER-QZ77";
        var ex = Assert.Throws<ImportFormatException>(() =>
            Read(OfxTestFile.Itau().Line(Day, "abc", "PIX " + marker)));
        Assert.Contains("malformed", ex.Message);
        Assert.Contains("line", ex.Message);
        Assert.DoesNotContain(marker, ex.Message);
    }

    // ---- integration (Postgres) ----

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    [Fact]
    public async Task Itau_ofx_upload_into_checking_account_previews_and_confirms()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var ok = await CreateAccountAsync(c, externalId: "00012345");
        var file = OfxTestFile.Itau().Line(Day, "-12.30", "PIX QRS PADARIA FICTICIA").Line(Day, "100.00", "PIX TRANSF FULANO FICTICIO").Build();

        var r = await UploadAsync(c, ok, file);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await JsonOf(r);
        Assert.DoesNotContain(body.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "AccountIdentifierMismatch");
        var batchId = body.GetProperty("batchId").GetGuid();
        Assert.Equal(2, (await ConfirmOkAsync(c, batchId)).GetProperty("created").GetInt32());

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var lines = await db.Transactions.OrderBy(t => t.Amount).ToListAsync();
            Assert.Equal(64, lines[0].SourceKey?.Length);
            Assert.Equal(TransactionState.Normal, lines[0].State);
            Assert.Equal(TransactionState.PendingClassification, lines[1].State);
        }

        var wrong = await CreateAccountAsync(c, externalId: "99999");
        var rw = await UploadAsync(c, wrong, OfxTestFile.Itau().Line(Day, "-3.00", "PIX QRS OUTRA FICTICIA").Build());
        Assert.Equal(HttpStatusCode.OK, rw.StatusCode);
        var bw = await JsonOf(rw);
        var warning = Assert.Single(bw.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "AccountIdentifierMismatch");
        Assert.Equal("blocking", warning.GetProperty("severity").GetString());
        var bid = bw.GetProperty("batchId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(c, bid)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(c, bid, acknowledge: true)).StatusCode);
    }

    [Fact]
    public async Task Itau_ofx_into_credit_card_account_is_account_type_mismatch()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var card = await CreateAccountAsync(c, "CreditCard");
        var r = await UploadAsync(c, card, OfxTestFile.Itau().Line(Day, "-1.00", "PIX QRS X").Build());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }

    [Fact]
    public async Task Itau_ledger_mismatch_against_previous_checkpoint_warns()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        Assert.Equal(HttpStatusCode.Created,
            (await c.PostAsync($"/api/accounts/{acc}/checkpoints", new { date = "2026-09-30", amount = "100.00" })).StatusCode);

        var r = await UploadAsync(c, acc,
            OfxTestFile.Itau().Line(Day, "-10.00", "PIX QRS X").Ledger("100.00", "20261031000000").Build());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var w = Assert.Single((await JsonOf(r)).GetProperty("warnings").EnumerateArray(),
            x => x.GetProperty("code").GetString() == "BalanceMismatch");
        Assert.Equal("warning", w.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Itau_ledger_before_last_line_creates_no_checkpoint_on_confirm()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var batchId = await PreviewAsync(c, acc,
            OfxTestFile.Itau().Line("20261031000000", "-10.00", "PIX QRS X").Ledger("77.00", "20261015000000").Build());
        await ConfirmOkAsync(c, batchId);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Empty(await db.Checkpoints.ToListAsync());
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal(77m, batch.LedgerBalance);
        Assert.Equal(new DateOnly(2026, 10, 15), batch.LedgerBalanceDate);
    }
}
