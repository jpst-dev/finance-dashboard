using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Tests.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Linking;

/// <summary>Links API, link seal on lines, pending queue and summary, through HTTP on Postgres (synthetic files only).</summary>
[Collection(PostgresCollection.Name)]
public sealed class LinkApiTests(PostgresFixture postgres)
{
    private const string Pay = "Pagamento de fatura";
    private const string Received = "Pagamento recebido";
    private const string ItauPix = "PIX TRANSF FULANO FICTICIO";
    private const string NuPixIn = "Transferência recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X";

    private async Task<ApiFactory> NewFactoryAsync() => new(await postgres.CreateDatabaseAsync());

    private static string Ofx(int day) => $"202610{day:00}000000[-3:BRT]";

    private static async Task<Guid> AccountAsync(AuthedClient c, string name, string type)
    {
        var r = await c.PostAsync("/api/accounts", new { name, type, institution = "Banco" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    private static async Task ImportAsync(AuthedClient c, Guid account, byte[] file)
    {
        var batch = await PreviewAsync(c, account, file);
        await ConfirmOkAsync(c, batch, acknowledge: true);
    }

    /// <summary>Nubank invoice payment: checking -500 and card +500 on the same day, auto-confirmed.</summary>
    private static async Task<(Guid Checking, Guid Card)> ConfirmedInvoiceAsync(AuthedClient c)
    {
        var checking = await AccountAsync(c, "Nu conta", "Checking");
        var card = await AccountAsync(c, "Nu cartao", "CreditCard");
        await ImportAsync(c, checking, OfxTestFile.Checking().Line(Ofx(10), "-500.00", Pay).Build());
        await ImportAsync(c, card, OfxTestFile.Card().Line(Ofx(10), "500.00", Received).Build());
        return (checking, card);
    }

    /// <summary>Itaú to Nubank Pix two days apart: stays Suggested.</summary>
    private static async Task SuggestedTransferAsync(AuthedClient c)
    {
        var itau = await AccountAsync(c, "Itau conta", "Checking");
        var nu = await AccountAsync(c, "Nu conta 2", "Checking");
        await ImportAsync(c, itau, OfxTestFile.Itau().Line(Ofx(10), "-100.00", ItauPix).Build());
        await ImportAsync(c, nu, OfxTestFile.Checking().Line(Ofx(12), "100.00", NuPixIn).Build());
    }

    private static async Task<JsonElement> LinksAsync(AuthedClient c, string query = "")
    {
        var r = await c.GetAsync("/api/links" + query);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    private static async Task<Guid> SingleLinkIdAsync(AuthedClient c, string state)
    {
        var page = await LinksAsync(c, $"?state={state}");
        return Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> TransactionsAsync(AuthedClient c, string query = "")
    {
        var r = await c.GetAsync("/api/transactions" + query);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await JsonOf(r)).GetProperty("items").EnumerateArray().ToList();
    }

    private static JsonElement ByAccount(List<JsonElement> items, Guid account) =>
        items.Single(i => i.GetProperty("accountId").GetGuid() == account);

    private static async Task<JsonElement> SummaryAsync(AuthedClient c) =>
        await JsonOf(await c.GetAsync("/api/summary?month=2026-10"));

    [Fact]
    public async Task List_filters_by_state_and_paginates()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await ConfirmedInvoiceAsync(c);
        await SuggestedTransferAsync(c);

        var all = await LinksAsync(c);
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal(1, (await LinksAsync(c, "?state=Confirmed")).GetProperty("total").GetInt32());
        var suggested = await LinksAsync(c, "?state=Suggested");
        Assert.Equal(1, suggested.GetProperty("total").GetInt32());

        var link = suggested.GetProperty("items")[0];
        Assert.Equal("OwnTransfer", link.GetProperty("kind").GetString());
        Assert.Equal("Suggested", link.GetProperty("state").GetString());
        Assert.Equal("Auto", link.GetProperty("origin").GetString());
        Assert.Equal("-100.00", link.GetProperty("out").GetProperty("amount").GetString());
        Assert.Equal("100.00", link.GetProperty("in").GetProperty("amount").GetString());

        var second = await LinksAsync(c, "?page=2&size=1");
        Assert.Equal(2, second.GetProperty("total").GetInt32());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/links?page=0")).StatusCode);
    }

    [Fact]
    public async Task Confirm_and_undo_happy_paths()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await ConfirmedInvoiceAsync(c);
        await SuggestedTransferAsync(c);

        var suggestedId = await SingleLinkIdAsync(c, "Suggested");
        var confirm = await c.PostAsync($"/api/links/{suggestedId}/confirm");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var body = await JsonOf(confirm);
        Assert.Equal("Confirmed", body.GetProperty("state").GetString());
        Assert.Equal("User", body.GetProperty("origin").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/links/{suggestedId}")).StatusCode);
        Assert.Equal(1, (await LinksAsync(c, "?state=Rejected")).GetProperty("total").GetInt32());

        // An undone pair is remembered as rejected: reconcile does not bring it back.
        var invoiceId = await SingleLinkIdAsync(c, "Confirmed");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/links/{invoiceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/links/reconcile")).StatusCode);
        Assert.Equal(0, (await LinksAsync(c, "?state=Confirmed")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await LinksAsync(c, "?state=Suggested")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Reject_marks_suggestion_rejected()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await SuggestedTransferAsync(c);

        var id = await SingleLinkIdAsync(c, "Suggested");
        var r = await c.PostAsync($"/api/links/{id}/reject");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Rejected", (await JsonOf(r)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Unknown_and_other_users_links_are_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        await SuggestedTransferAsync(a);
        var id = await SingleLinkIdAsync(a, "Suggested");

        foreach (var target in new[] { id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/links/{target}/confirm")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/links/{target}/reject")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/links/{target}")).StatusCode);
        }

        Assert.Equal(0, (await LinksAsync(b)).GetProperty("total").GetInt32());
        Assert.Equal(1, (await LinksAsync(a, "?state=Suggested")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Wrong_state_is_409()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await ConfirmedInvoiceAsync(c);
        await SuggestedTransferAsync(c);
        var confirmed = await SingleLinkIdAsync(c, "Confirmed");
        var suggested = await SingleLinkIdAsync(c, "Suggested");

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/links/{confirmed}/confirm")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/links/{confirmed}/reject")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/links/{suggested}")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        await using var f = await NewFactoryAsync();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/links")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/links/reconcile", null)).StatusCode);
    }

    [Fact]
    public async Task Writes_require_antiforgery_token()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await SuggestedTransferAsync(c);
        var id = await SingleLinkIdAsync(c, "Suggested");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.PostAsync($"/api/links/{id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.PostAsync($"/api/links/{id}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.PostAsync("/api/links/reconcile", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.DeleteAsync($"/api/links/{id}")).StatusCode);
        Assert.Equal(1, (await LinksAsync(c, "?state=Suggested")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Reconcile_endpoint_is_idempotent()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await ConfirmedInvoiceAsync(c);
        await SuggestedTransferAsync(c);

        for (var i = 0; i < 2; i++)
        {
            var r = await c.PostAsync("/api/links/reconcile");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonOf(r);
            Assert.Equal(0, body.GetProperty("linked").GetInt32());
            Assert.Equal(1, body.GetProperty("suggested").GetInt32());
        }

        Assert.Equal(2, (await LinksAsync(c)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Transaction_response_carries_link()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var (checking, card) = await ConfirmedInvoiceAsync(c);
        var items = await TransactionsAsync(c);
        var checkingLine = ByAccount(items, checking);
        var cardLine = ByAccount(items, card);

        var link = checkingLine.GetProperty("link");
        Assert.Equal("InvoicePayment", link.GetProperty("kind").GetString());
        Assert.Equal("Confirmed", link.GetProperty("state").GetString());
        Assert.Equal(cardLine.GetProperty("id").GetGuid(), link.GetProperty("counterpartTransactionId").GetGuid());
        Assert.Equal(checkingLine.GetProperty("id").GetGuid(),
            cardLine.GetProperty("link").GetProperty("counterpartTransactionId").GetGuid());

        // The edit response carries the seal too (a description-only edit keeps the link).
        var put = await c.PutAsync($"/api/transactions/{checkingLine.GetProperty("id").GetGuid()}", new
        {
            date = checkingLine.GetProperty("date").GetString(),
            amount = checkingLine.GetProperty("amount").GetString(),
            description = "Fatura do cartao",
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("Confirmed", (await JsonOf(put)).GetProperty("link").GetProperty("state").GetString());

        // A manual line has no link.
        var created = await c.PostAsync("/api/transactions",
            new { accountId = checking, date = "2026-10-11", amount = "-1.00", description = "x" });
        Assert.Equal(JsonValueKind.Null, (await JsonOf(created)).GetProperty("link").ValueKind);
    }

    [Fact]
    public async Task Suggested_link_shows_on_pix_lines_that_are_normal_now()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await SuggestedTransferAsync(c);

        // Pix is Normal since S3(b): the pair is no longer in the pending queue, but its lines carry the suggestion.
        Assert.Empty(await TransactionsAsync(c, "?state=PendingClassification"));
        var all = await TransactionsAsync(c);
        Assert.Equal(2, all.Count);
        Assert.All(all, p => Assert.Equal("Suggested", p.GetProperty("link").GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Pending_list_and_summary_exclude_confirmed_linked_lines()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        await ConfirmedInvoiceAsync(c);

        Assert.Empty(await TransactionsAsync(c, "?state=PendingClassification"));
        Assert.Equal(2, (await TransactionsAsync(c)).Count);
        Assert.Equal(0, (await SummaryAsync(c)).GetProperty("pending").GetProperty("count").GetInt32());

        await c.DeleteAsync($"/api/links/{await SingleLinkIdAsync(c, "Confirmed")}");

        Assert.Equal(2, (await TransactionsAsync(c, "?state=PendingClassification")).Count);
        Assert.Equal(2, (await SummaryAsync(c)).GetProperty("pending").GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("state")]
    public async Task Edit_amount_date_or_state_deletes_link_and_returns_line_to_queue(string field)
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var (checking, card) = await ConfirmedInvoiceAsync(c);
        var line = ByAccount(await TransactionsAsync(c), checking);

        var put = await c.PutAsync($"/api/transactions/{line.GetProperty("id").GetGuid()}", new
        {
            date = field == "date" ? "2026-10-25" : line.GetProperty("date").GetString(),
            amount = field == "amount" ? "-501.00" : line.GetProperty("amount").GetString(),
            description = line.GetProperty("description").GetString(),
            state = field == "state" ? "Normal" : null,
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(0, (await LinksAsync(c, "?state=Confirmed")).GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await JsonOf(put)).GetProperty("link").ValueKind);
        var queue = await TransactionsAsync(c, "?state=PendingClassification");
        Assert.Contains(queue, q => q.GetProperty("accountId").GetGuid() == card);
        if (field != "state") Assert.Contains(queue, q => q.GetProperty("accountId").GetGuid() == checking);
    }

    [Fact]
    public async Task Hide_line_deletes_link()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var (checking, card) = await ConfirmedInvoiceAsync(c);
        var line = ByAccount(await TransactionsAsync(c), checking);

        var del = await c.DeleteAsync($"/api/transactions/{line.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(0, (await LinksAsync(c)).GetProperty("total").GetInt32());
        var queue = await TransactionsAsync(c, "?state=PendingClassification");
        Assert.Equal(card, Assert.Single(queue).GetProperty("accountId").GetGuid());
        Assert.Equal(JsonValueKind.Null, queue[0].GetProperty("link").ValueKind);
    }

    [Fact]
    public async Task Balances_and_income_expenses_unchanged_by_links()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var (checking, _) = await ConfirmedInvoiceAsync(c);
        var manual = await c.PostAsync("/api/transactions",
            new { accountId = checking, date = "2026-10-12", amount = "-20.00", description = "Padaria" });
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);

        var summaryLinked = await SummaryAsync(c);
        var accountsLinked = await (await c.GetAsync("/api/accounts")).Content.ReadAsStringAsync();
        await c.DeleteAsync($"/api/links/{await SingleLinkIdAsync(c, "Confirmed")}");
        var summaryUnlinked = await SummaryAsync(c);
        var accountsUnlinked = await (await c.GetAsync("/api/accounts")).Content.ReadAsStringAsync();

        Assert.Equal(accountsLinked, accountsUnlinked);
        foreach (var key in new[] { "income", "expenses", "net" })
            Assert.Equal(summaryLinked.GetProperty(key).GetString(), summaryUnlinked.GetProperty(key).GetString());
        Assert.Equal("-20.00", summaryLinked.GetProperty("expenses").GetString());
    }
}
