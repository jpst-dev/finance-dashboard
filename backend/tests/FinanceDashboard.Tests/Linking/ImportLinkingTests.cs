using System.Net;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Linking;

/// <summary>Reconciliation at import confirm, through HTTP on Postgres (synthetic files only).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportLinkingTests(PostgresFixture postgres)
{
    private const string Pay = "Pagamento de fatura";
    private const string Received = "Pagamento recebido";
    private const string NuPixOut = "Transferência enviada pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X";
    private const string ItauPix = "PIX TRANSF FULANO FICTICIO";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static string Ofx(int day) => $"202610{day:00}000000[-3:BRT]";

    private static byte[] NuChecking(string memo, string amount, int day) =>
        OfxTestFile.Checking().Line(Ofx(day), amount, memo).Build();

    private static byte[] NuCard(string memo, string amount, int day) =>
        OfxTestFile.Card().Line(Ofx(day), amount, memo).Build();

    private static byte[] ItauChecking(string memo, string amount, int day) =>
        OfxTestFile.Itau().Line(Ofx(day), amount, memo).Build();

    /// <summary>Itaú invoice with one ordinary purchase (so the declared total holds) and one payment line.</summary>
    private static byte[] ItauInvoice(string paymentAmount, int day) =>
        XlsxTestFile.Invoice("2026-11-10", total: "10.00")
            .Line("2026-10-03", "LOJA FICTICIA", "10.00")
            .Line($"2026-10-{day:00}", "PAGAMENTO EFETUADO", paymentAmount)
            .Build();

    private static async Task<Guid> AccountAsync(AuthedClient c, string name, string type)
    {
        var r = await c.PostAsync("/api/accounts", new { name, type, institution = "Banco" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    /// <summary>Preview + confirm of one file; returns the batch id and the confirm response body.</summary>
    private static async Task<(Guid Batch, System.Text.Json.JsonElement Result)> ImportAsync(
        AuthedClient c, Guid account, byte[] file, string name = "arquivo.ofx")
    {
        var batch = await PreviewAsync(c, account, file, name);
        return (batch, await ConfirmOkAsync(c, batch, acknowledge: true));
    }

    private static (int Linked, int Suggested) Counts((Guid, System.Text.Json.JsonElement R) x) =>
        (x.R.GetProperty("linked").GetInt32(), x.R.GetProperty("suggested").GetInt32());

    private static async Task<List<Link>> LinksAsync(ApiFactory f, AuthedClient c)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        return await db.Links.AsNoTracking().ToListAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nubank_invoice_payment_links_after_both_files_in_either_order(bool checkingFirst)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await AccountAsync(c, "Nu conta", "Checking");
        var card = await AccountAsync(c, "Nu cartao", "CreditCard");
        var checkingFile = NuChecking(Pay, "-500.00", 10);
        var cardFile = NuCard(Received, "500.00", 10);

        var first = checkingFirst ? await ImportAsync(c, checking, checkingFile) : await ImportAsync(c, card, cardFile);
        Assert.Equal((0, 0), Counts(first));
        var second = checkingFirst ? await ImportAsync(c, card, cardFile) : await ImportAsync(c, checking, checkingFile);
        Assert.Equal((1, 0), Counts(second));

        var link = Assert.Single(await LinksAsync(f, c));
        Assert.Equal(LinkKind.InvoicePayment, link.Kind);
        Assert.Equal(LinkState.Confirmed, link.State);
        Assert.Equal(LinkOrigin.Auto, link.Origin);
    }

    [Fact]
    public async Task Itau_invoice_payment_links_checking_and_card_file()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await AccountAsync(c, "Itau conta", "Checking");
        var card = await AccountAsync(c, "Itau cartao", "CreditCard");

        await ImportAsync(c, checking, ItauChecking("FATURA PAGA ITAU UNICLASS", "-500.00", 10));
        var second = await ImportAsync(c, card, ItauInvoice("-500.00", 10), "fatura.xlsx");

        Assert.Equal((1, 0), Counts(second));
        var link = Assert.Single(await LinksAsync(f, c));
        Assert.Equal(LinkKind.InvoicePayment, link.Kind);
        Assert.Equal(LinkState.Confirmed, link.State);
    }

    [Fact]
    public async Task Itau_to_nubank_pix_transfer_links_when_same_day()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var itau = await AccountAsync(c, "Itau conta", "Checking");
        var nu = await AccountAsync(c, "Nu conta", "Checking");

        await ImportAsync(c, itau, ItauChecking(ItauPix, "-100.00", 10));
        var second = await ImportAsync(c, nu, NuChecking("Transferência recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", "100.00", 10));

        Assert.Equal((1, 0), Counts(second));
        var link = Assert.Single(await LinksAsync(f, c));
        Assert.Equal(LinkKind.OwnTransfer, link.Kind);
        Assert.Equal(LinkState.Confirmed, link.State);
    }

    [Fact]
    public async Task Itau_to_nubank_pix_transfer_suggested_when_two_days_apart()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var itau = await AccountAsync(c, "Itau conta", "Checking");
        var nu = await AccountAsync(c, "Nu conta", "Checking");

        await ImportAsync(c, itau, ItauChecking(ItauPix, "-100.00", 10));
        var second = await ImportAsync(c, nu, NuChecking("Transferência recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", "100.00", 12));

        Assert.Equal((0, 1), Counts(second));
        Assert.Equal(LinkState.Suggested, Assert.Single(await LinksAsync(f, c)).State);
    }

    [Fact]
    public async Task Confirm_reports_linked_and_suggested_counts()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var nuChecking = await AccountAsync(c, "Nu conta", "Checking");
        var nuCard = await AccountAsync(c, "Nu cartao", "CreditCard");
        var itau = await AccountAsync(c, "Itau conta", "Checking");

        var checkingFile = OfxTestFile.Checking()
            .Line(Ofx(10), "-500.00", Pay)
            .Line(Ofx(10), "-100.00", NuPixOut).Build();

        Assert.Equal((0, 0), Counts(await ImportAsync(c, nuChecking, checkingFile)));
        // Pix received two days later: only suggested.
        Assert.Equal((0, 1), Counts(await ImportAsync(c, itau, ItauChecking(ItauPix, "100.00", 12))));
        // The card payment links; the suggestion is still there and still counted.
        var third = await ImportAsync(c, nuCard, NuCard(Received, "500.00", 10));
        Assert.Equal((1, 1), Counts(third));

        var links = await LinksAsync(f, c);
        Assert.Equal(1, links.Count(l => l.State == LinkState.Confirmed));
        Assert.Equal(1, links.Count(l => l.State == LinkState.Suggested));
    }

    [Fact]
    public async Task Undo_batch_removes_its_links_and_reimport_relinks()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await AccountAsync(c, "Nu conta", "Checking");
        var card = await AccountAsync(c, "Nu cartao", "CreditCard");
        var checkingFile = NuChecking(Pay, "-500.00", 10);
        var cardFile = NuCard(Received, "500.00", 10);

        await ImportAsync(c, checking, checkingFile);
        var (cardBatch, _) = await ImportAsync(c, card, cardFile);
        Assert.Single(await LinksAsync(f, c));

        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, cardBatch)).StatusCode);
        Assert.Empty(await LinksAsync(f, c));

        // The same card file imported again pairs with the checking line that is still there.
        Assert.Equal((1, 0), Counts(await ImportAsync(c, card, cardFile)));
        Assert.Equal(LinkState.Confirmed, Assert.Single(await LinksAsync(f, c)).State);
    }

    [Fact]
    public async Task Rejected_link_survives_next_import_but_not_batch_undo()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var itau = await AccountAsync(c, "Itau conta", "Checking");
        var nu = await AccountAsync(c, "Nu conta", "Checking");

        await ImportAsync(c, itau, ItauChecking(ItauPix, "-100.00", 10));
        var (nuBatch, _) = await ImportAsync(c, nu,
            NuChecking("Transferência recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", "100.00", 12));
        Guid linkId;
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var link = await db.Links.SingleAsync();
            link.State = LinkState.Rejected;
            await db.SaveChangesAsync();
            linkId = link.Id;
        }

        // An unrelated import reconciles again: the rejection stays and the pair is not suggested again.
        var other = await ImportAsync(c, itau, ItauChecking("TARIFA FICTICIA", "-1.00", 11));
        Assert.Equal((0, 0), Counts(other));
        var kept = Assert.Single(await LinksAsync(f, c));
        Assert.Equal(linkId, kept.Id);
        Assert.Equal(LinkState.Rejected, kept.State);

        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, nuBatch)).StatusCode);
        Assert.Empty(await LinksAsync(f, c));
    }

    [Fact]
    public async Task Parcial_and_larger_payments_stay_unlinked_and_pending()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await AccountAsync(c, "Nu conta", "Checking");
        var card = await AccountAsync(c, "Nu cartao", "CreditCard");

        await ImportAsync(c, checking, OfxTestFile.Checking()
            .Line(Ofx(10), "-300.00", Pay)      // partial
            .Line(Ofx(11), "-800.00", Pay).Build()); // larger
        var second = await ImportAsync(c, card, NuCard(Received, "500.00", 10));

        Assert.Equal((0, 0), Counts(second));
        Assert.Empty(await LinksAsync(f, c));
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.All(await db.Transactions.AsNoTracking().ToListAsync(),
            t => Assert.Equal(TransactionState.PendingClassification, t.State));
    }

    [Fact]
    public async Task Invoice_payment_from_other_bank_stays_pending()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var itau = await AccountAsync(c, "Itau conta", "Checking");
        var nuCard = await AccountAsync(c, "Nu cartao", "CreditCard");

        await ImportAsync(c, itau, ItauChecking("FATURA PAGA ITAU UNICLASS", "-500.00", 10));
        var second = await ImportAsync(c, nuCard, NuCard(Received, "500.00", 10));

        Assert.Equal((0, 0), Counts(second));
        Assert.Empty(await LinksAsync(f, c));
    }

    /// <summary>
    /// One side of the pair per confirm, fired together. Deterministic: each confirm inserts its line, then takes the
    /// per-user advisory lock and reads at that point (read committed). The one that locks first sees only its own side
    /// (the other is uncommitted), links nothing and commits; the second waits for the lock, then sees both and links.
    /// So exactly one Confirmed link and linked == 1 in total, whatever the order.
    /// </summary>
    [Fact]
    public async Task Concurrent_confirms_never_create_two_confirmed_for_one_line()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await AccountAsync(c, "Nu conta", "Checking");
        var card = await AccountAsync(c, "Nu cartao", "CreditCard");
        var a = await PreviewAsync(c, checking, NuChecking(Pay, "-500.00", 10));
        var b = await PreviewAsync(c, card, NuCard(Received, "500.00", 10));

        var responses = await RaceAsync(c, [
            ($"/api/imports/{a}/confirm", new { acknowledgeBlockingWarnings = false }),
            ($"/api/imports/{b}/confirm", new { acknowledgeBlockingWarnings = false })]);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(JsonOf));
        Assert.Equal(1, bodies.Sum(x => x.GetProperty("linked").GetInt32()));
        Assert.Equal(0, bodies.Sum(x => x.GetProperty("suggested").GetInt32()));

        var links = await LinksAsync(f, c);
        var confirmed = Assert.Single(links);
        Assert.Equal(LinkState.Confirmed, confirmed.State);
        Assert.Equal(links.Count, links.Select(l => l.OutTransactionId).Distinct().Count());
        Assert.Equal(links.Count, links.Select(l => l.InTransactionId).Distinct().Count());
    }

    [Fact]
    public async Task Reconcile_failure_rolls_back_the_whole_confirm()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await AccountAsync(c, "Nu conta", "Checking");
        var card = await AccountAsync(c, "Nu cartao", "CreditCard");
        await ImportAsync(c, checking, NuChecking(Pay, "-500.00", 10));
        var batch = await PreviewAsync(c, card, NuCard(Received, "500.00", 10));

        // Reconcile inserts the link last; make that insert fail.
        await using (var conn = new NpgsqlConnection(f.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE FUNCTION fail_link() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'boom'; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER fail_link BEFORE INSERT ON "Links" FOR EACH ROW EXECUTE FUNCTION fail_link();
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        HttpStatusCode? status = null;
        try
        {
            status = (await ConfirmAsync(c, batch, acknowledge: true)).StatusCode;
        }
        catch (Exception)
        {
            // The test server may surface the unhandled exception instead of a 500.
        }

        if (status is { } code) Assert.Equal(HttpStatusCode.InternalServerError, code);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch)).State);
        Assert.Equal(0, await db.Transactions.CountAsync(t => t.BatchId == batch));
        Assert.Empty(await db.Links.ToListAsync());
    }
}
