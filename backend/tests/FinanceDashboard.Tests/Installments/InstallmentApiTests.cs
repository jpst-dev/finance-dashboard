using System.Net;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Installments;

/// <summary>GET /api/installments through HTTP on Postgres with a fixed clock (synthetic data only).</summary>
[Collection(PostgresCollection.Name)]
public sealed class InstallmentApiTests(PostgresFixture postgres)
{
    // 2026-11-03 12:00 in Sao Paulo (UTC-3).
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    private async Task<ApiFactory> NewFactoryAsync() =>
        new(await postgres.CreateDatabaseAsync()) { TimeProvider = new FakeTimeProvider(Now) };

    private sealed class World(ApiFactory f, string userId)
    {
        private Guid? _batch;

        public async Task<Guid> AccountAsync(string name, AccountType type = AccountType.CreditCard, bool archived = false)
        {
            await using var db = PersistenceTestSupport.Create(f.ConnectionString, userId);
            var a = new Account
            {
                Id = Guid.NewGuid(), Name = name, Institution = "Banco Exemplo", Type = type, IsArchived = archived,
            };
            db.Accounts.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        }

        public async Task LineAsync(Guid account, string description, decimal amount, DateOnly? statement, int? n = null,
            int? t = null, string? suffix = "1234", bool manual = false, bool deleted = false)
        {
            await using var db = PersistenceTestSupport.Create(f.ConnectionString, userId);
            if (!manual && _batch is null)
            {
                var batch = new ImportBatch
                {
                    Id = Guid.NewGuid(), AccountId = account, Source = ImportSource.ItauCard,
                    FileHash = Guid.NewGuid().ToString("N"), State = ImportBatchState.Confirmed,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ImportBatches.Add(batch);
                await db.SaveChangesAsync();
                _batch = batch.Id;
            }

            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), AccountId = account, Date = new DateOnly(2026, 10, 5), StatementDate = statement,
                Amount = amount, OriginalDescription = description, NormalizedDescription = description,
                State = TransactionState.Normal, Origin = manual ? TransactionOrigin.Manual : TransactionOrigin.Imported,
                BatchId = manual ? null : _batch, IsDeleted = deleted, InstallmentNumber = n, InstallmentTotal = t,
                CardSuffix = suffix,
            });
            await db.SaveChangesAsync();
        }
    }

    private static readonly DateOnly Stmt = new(2026, 11, 10);

    private static async Task<JsonElement> GetOkAsync(AuthedClient c)
    {
        var r = await c.GetAsync("/api/installments");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    [Fact]
    public async Task Projects_future_installments_with_money_as_strings()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA EXEMPLO 02/04", -100m, Stmt, 2, 4);

        var body = await GetOkAsync(c);
        Assert.Equal("200.00", body.GetProperty("totalCommitted").GetString());
        var acc = Assert.Single(body.GetProperty("accounts").EnumerateArray());
        Assert.Equal(card, acc.GetProperty("accountId").GetGuid());
        Assert.Equal("Cartao Exemplo", acc.GetProperty("accountName").GetString());
        Assert.Equal("2026-11-10", acc.GetProperty("basedOn").GetString());

        var months = body.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(2, months.Count);
        Assert.Equal("2026-11", months[0].GetProperty("month").GetString());
        Assert.Equal("100.00", months[0].GetProperty("total").GetString());
        var item = Assert.Single(months[0].GetProperty("items").EnumerateArray());
        Assert.Equal(3, item.GetProperty("installmentNumber").GetInt32());
        Assert.Equal(4, item.GetProperty("installmentTotal").GetInt32());
        Assert.Equal("100.00", item.GetProperty("amount").GetString());
        Assert.Equal("1234", item.GetProperty("cardSuffix").GetString());
        Assert.Equal("2026-12", months[1].GetProperty("month").GetString());
        Assert.Equal("100.00", months[1].GetProperty("total").GetString());
        var last = Assert.Single(months[1].GetProperty("items").EnumerateArray());
        Assert.Equal(4, last.GetProperty("installmentNumber").GetInt32());
        Assert.Equal(4, last.GetProperty("installmentTotal").GetInt32());

        var g = Assert.Single(body.GetProperty("groups").EnumerateArray());
        Assert.Equal(2, g.GetProperty("remaining").GetInt32());
        Assert.Equal(1, g.GetProperty("multiplicity").GetInt32());
        Assert.Equal(2, g.GetProperty("installmentNumber").GetInt32());
        Assert.Equal("2026-12-10", g.GetProperty("endsOn").GetString());
        Assert.Equal("200.00", g.GetProperty("remainingTotal").GetString());
        Assert.Equal("100.00", g.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Manual_and_payment_lines_do_not_define_the_base()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA EXEMPLO 02/04", -100m, Stmt, 2, 4);
        await w.LineAsync(card, "LOJA MANUAL 01/03", -50m, new DateOnly(2027, 3, 10), 1, 3, manual: true);
        await w.LineAsync(card, "PAGAMENTO RECEBIDO 01/03", 80m, new DateOnly(2027, 3, 10), 1, 3);

        var body = await GetOkAsync(c);
        Assert.Equal("200.00", body.GetProperty("totalCommitted").GetString());
        Assert.Equal("2026-11-10", Assert.Single(body.GetProperty("accounts").EnumerateArray()).GetProperty("basedOn").GetString());
        Assert.Single(body.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Hidden_and_deleted_lines_are_ignored()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA APAGADA 01/03", -50m, Stmt, 1, 3, deleted: true);

        var body = await GetOkAsync(c);
        Assert.Equal("0.00", body.GetProperty("totalCommitted").GetString());
        Assert.Equal(0, body.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task Archived_account_is_included()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Antigo", archived: true);
        await w.LineAsync(card, "LOJA EXEMPLO 02/04", -100m, Stmt, 2, 4);

        var body = await GetOkAsync(c);
        Assert.Equal("200.00", body.GetProperty("totalCommitted").GetString());
        Assert.Single(body.GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task Other_user_lines_are_not_returned()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var wa = new World(f, a.UserId);
        var card = await wa.AccountAsync("Cartao Exemplo");
        await wa.LineAsync(card, "LOJA EXEMPLO 02/04", -100m, Stmt, 2, 4);

        var mine = await GetOkAsync(a);
        Assert.Equal("200.00", mine.GetProperty("totalCommitted").GetString());
        var theirs = await GetOkAsync(b);
        Assert.Equal("0.00", theirs.GetProperty("totalCommitted").GetString());
        Assert.Equal(0, theirs.GetProperty("groups").GetArrayLength());
        Assert.Equal(0, theirs.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task Newer_invoice_without_installments_is_the_base_and_projects_nothing()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA EXEMPLO 03/04", -100m, new DateOnly(2026, 10, 10), 3, 4);
        await w.LineAsync(card, "Compra a vista Exemplo", -40m, Stmt);

        var body = await GetOkAsync(c);
        Assert.Equal("0.00", body.GetProperty("totalCommitted").GetString());
        Assert.Equal("2026-11-10", Assert.Single(body.GetProperty("accounts").EnumerateArray()).GetProperty("basedOn").GetString());
        Assert.Equal(0, body.GetProperty("groups").GetArrayLength());
        Assert.Equal(0, body.GetProperty("months").GetArrayLength());
    }

    [Fact]
    public async Task Deleted_line_on_a_newer_invoice_does_not_move_the_base()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA EXEMPLO 02/04", -100m, Stmt, 2, 4);
        await w.LineAsync(card, "Compra apagada", -40m, new DateOnly(2026, 12, 10), deleted: true);

        var body = await GetOkAsync(c);
        Assert.Equal("2026-11-10", Assert.Single(body.GetProperty("accounts").EnumerateArray()).GetProperty("basedOn").GetString());
        Assert.Equal("200.00", body.GetProperty("totalCommitted").GetString());
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        await using var f = await NewFactoryAsync();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/installments")).StatusCode);
    }

    [Fact]
    public async Task Stale_base_cuts_past_months_and_reports_basedOn()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA ANTIGA 01/05", -30m, new DateOnly(2026, 8, 10), 1, 5);

        var body = await GetOkAsync(c);
        Assert.Equal("2026-08-10", Assert.Single(body.GetProperty("accounts").EnumerateArray()).GetProperty("basedOn").GetString());
        var month = Assert.Single(body.GetProperty("months").EnumerateArray());
        Assert.Equal("2026-11", month.GetProperty("month").GetString());
        Assert.Equal("30.00", body.GetProperty("totalCommitted").GetString());
        var item = Assert.Single(month.GetProperty("items").EnumerateArray());
        Assert.Equal(5, item.GetProperty("installmentNumber").GetInt32());
    }

    [Fact]
    public async Task Nubank_card_lines_without_installments_are_ignored()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Nu");
        await w.LineAsync(card, "Compra a vista Exemplo", -40m, Stmt, suffix: null);
        await w.LineAsync(card, "Compra unica 01/01", -40m, Stmt, 1, 1, suffix: null);
        await w.LineAsync(card, "Sem fatura", -40m, null, 1, 3);

        var body = await GetOkAsync(c);
        Assert.Equal("0.00", body.GetProperty("totalCommitted").GetString());
        Assert.Equal(0, body.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task Non_card_accounts_are_ignored()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var checking = await w.AccountAsync("Conta Corrente", AccountType.Checking);
        await w.LineAsync(checking, "LOJA EXEMPLO 02/04", -100m, Stmt, 2, 4);

        Assert.Equal("0.00", (await GetOkAsync(c)).GetProperty("totalCommitted").GetString());
    }

    [Fact]
    public async Task Empty_state_returns_empty_lists_and_zero_total()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        var body = await GetOkAsync(c);
        Assert.Equal("0.00", body.GetProperty("totalCommitted").GetString());
        Assert.Equal(0, body.GetProperty("accounts").GetArrayLength());
        Assert.Equal(0, body.GetProperty("months").GetArrayLength());
        Assert.Equal(0, body.GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public async Task Today_is_the_Sao_Paulo_date()
    {
        // 2026-12-01 01:00 UTC is still 2026-11-30 in Sao Paulo: the November installment must still count.
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync())
        {
            TimeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.Zero)),
        };
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var w = new World(f, c.UserId);
        var card = await w.AccountAsync("Cartao Exemplo");
        await w.LineAsync(card, "LOJA EXEMPLO 01/02", -100m, Stmt, 1, 2);

        var body = await GetOkAsync(c);
        Assert.Equal("100.00", body.GetProperty("totalCommitted").GetString());
        Assert.Equal("2026-11", Assert.Single(body.GetProperty("months").EnumerateArray()).GetProperty("month").GetString());
    }
}
