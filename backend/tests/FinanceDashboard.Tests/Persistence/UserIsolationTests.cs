using System.Net;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class UserIsolationTests(PostgresFixture postgres)
{
    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var db = PersistenceTestSupport.Create(cs, null);
        await db.Database.MigrateAsync();
        return cs;
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var account = new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I", Type = AccountType.Checking };
        var category = new Category { Id = Guid.NewGuid(), Name = "C", Kind = CategoryKind.Expense };
        var batch = new ImportBatch { Id = Guid.NewGuid(), AccountId = account.Id, FileHash = "h", Source = ImportSource.NubankChecking, State = ImportBatchState.Preview, CreatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(account);
        db.Categories.Add(category);
        db.ImportBatches.Add(batch);
        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, Date = new DateOnly(2026, 10, 1), Amount = -1m, OriginalDescription = "x", NormalizedDescription = "x" });
        db.Checkpoints.Add(new BalanceCheckpoint { Id = Guid.NewGuid(), AccountId = account.Id, Date = new DateOnly(2026, 10, 1), Amount = 1m, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Query_filter_hides_other_users_rows_for_every_entity()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a")) await SeedAsync(a);

        await using var b = PersistenceTestSupport.Create(cs, "user-b");
        Assert.Empty(await b.Accounts.ToListAsync());
        Assert.Empty(await b.Categories.ToListAsync());
        Assert.Empty(await b.Transactions.ToListAsync());
        Assert.Empty(await b.Checkpoints.ToListAsync());
        Assert.Empty(await b.ImportBatches.ToListAsync());

        await using var a2 = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Single(await a2.Accounts.ToListAsync());
        Assert.Single(await a2.Categories.ToListAsync());
        Assert.Single(await a2.Transactions.ToListAsync());
        Assert.Single(await a2.Checkpoints.ToListAsync());
        Assert.Single(await a2.ImportBatches.ToListAsync());
        Assert.All(await a2.Accounts.ToListAsync(), x => Assert.Equal("user-a", x.UserId));
    }

    [Fact]
    public async Task Saving_without_current_user_throws()
    {
        var cs = await NewDbAsync();
        await using var db = PersistenceTestSupport.Create(cs, null);
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), Name = "A", Institution = "I" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task IgnoreQueryFilters_sees_all()
    {
        var cs = await NewDbAsync();
        await using (var a = PersistenceTestSupport.Create(cs, "user-a")) await SeedAsync(a);
        await using (var b = PersistenceTestSupport.Create(cs, "user-b")) await SeedAsync(b);

        await using var anon = PersistenceTestSupport.Create(cs, null);
        Assert.Empty(await anon.Accounts.ToListAsync());
        Assert.Equal(2, await anon.Accounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await anon.Transactions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Visible_excludes_soft_deleted_transactions()
    {
        var cs = await NewDbAsync();
        await using var db = PersistenceTestSupport.Create(cs, "user-a");
        await SeedAsync(db);
        var t = await db.Transactions.SingleAsync();
        t.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.Empty(await db.Transactions.Visible().ToListAsync());
        Assert.Single(await db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task Itau_files_are_isolated_between_users()
    {
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);

        var ofx = OfxTestFile.Itau().NoLedger()
            .Line("20261005000000", "-2.50", "PIX QRS CAFE FICTICIO").Line("20261005000000", "-2.50", "PIX QRS CAFE FICTICIO")
            .Line("20261006000000", "-12.30", "PIX QRS PADARIA FICTICIA")
            .Build();
        static XlsxTestFile Invoice(bool withB) => withB
            ? XlsxTestFile.Invoice("2026-11-10", total: "130.00").Line("2026-10-03", "LOJA A FICTICIA", "100.00")
                .Line("2026-10-04", "LOJA B FICTICIA", "30.00")
            : XlsxTestFile.Invoice("2026-11-10", total: "100.00").Line("2026-10-03", "LOJA A FICTICIA", "100.00");

        static string[] Codes(JsonElement preview) =>
            preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()!).ToArray();

        async Task<JsonElement> Preview(AuthedClient c, Guid account, byte[] file, string name)
        {
            var r = await UploadAsync(c, account, file, name);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return await JsonOf(r);
        }

        // A imports the statement and the full invoice.
        var aChecking = await CreateAccountAsync(a);
        var aCard = await CreateAccountAsync(a, "CreditCard");
        var aOfx = await Preview(a, aChecking, ofx, "extrato.ofx");
        var aInvoice = await Preview(a, aCard, Invoice(withB: true).Build(), "fatura.xlsx");
        var aOfxBatch = aOfx.GetProperty("batchId").GetGuid();
        var aInvoiceBatch = aInvoice.GetProperty("batchId").GetGuid();

        // B uploads the very same OFX bytes while A's preview is open: it must not replace A's preview.
        var bChecking = await CreateAccountAsync(b);
        var bCard = await CreateAccountAsync(b, "CreditCard");
        var bOfxEarly = await Preview(b, bChecking, ofx, "extrato.ofx");
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/imports/{aOfxBatch}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(b, bOfxEarly.GetProperty("batchId").GetGuid())).StatusCode);

        Assert.Equal(3, (await ConfirmOkAsync(a, aOfxBatch)).GetProperty("created").GetInt32());
        Assert.Equal(2, (await ConfirmOkAsync(a, aInvoiceBatch)).GetProperty("created").GetInt32());

        // After A confirmed: same OFX bytes and a smaller version of the same invoice for B.
        var bOfx = await Preview(b, bChecking, ofx, "extrato.ofx");
        Assert.Empty(Codes(bOfx)); // no AlreadyImported, FitidInOtherAccount, PossibleDuplicate, PeriodOverlap
        Assert.Equal(3, bOfx.GetProperty("newCount").GetInt32());
        Assert.All(bOfx.GetProperty("lines").EnumerateArray(), l => Assert.False(l.GetProperty("existing").GetBoolean()));
        var bInvoice = await Preview(b, bCard, Invoice(withB: false).Build(), "fatura.xlsx");
        Assert.Empty(Codes(bInvoice)); // A's line B is not "missing" from B's invoice
        Assert.Equal(1, bInvoice.GetProperty("newCount").GetInt32());

        // B cannot read, confirm or undo A's batches.
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/imports/{aOfxBatch}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/imports/{aInvoiceBatch}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UndoAsync(b, aOfxBatch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/imports/{bOfx.GetProperty("batchId").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(a, bOfx.GetProperty("batchId").GetGuid())).StatusCode);

        Assert.Equal(3, (await ConfirmOkAsync(b, bOfx.GetProperty("batchId").GetGuid())).GetProperty("created").GetInt32());
        Assert.Equal(1, (await ConfirmOkAsync(b, bInvoice.GetProperty("batchId").GetGuid())).GetProperty("created").GetInt32());

        // Each one sees only their own batches and lines.
        var aList = (await JsonOf(await a.GetAsync("/api/imports"))).EnumerateArray().Select(x => x.GetProperty("batchId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { aOfxBatch, aInvoiceBatch }, aList);
        var bList = (await JsonOf(await b.GetAsync("/api/imports"))).EnumerateArray().Select(x => x.GetProperty("batchId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { bOfx.GetProperty("batchId").GetGuid(), bInvoice.GetProperty("batchId").GetGuid() }, bList);
        Assert.Equal(5, (await JsonOf(await a.GetAsync("/api/transactions"))).GetProperty("total").GetInt32());
        Assert.Equal(4, (await JsonOf(await b.GetAsync("/api/transactions"))).GetProperty("total").GetInt32());

        await using var dbA = PersistenceTestSupport.Create(f.ConnectionString, a.UserId);
        Assert.All(await dbA.Transactions.ToListAsync(), t => Assert.Contains(t.AccountId, new[] { aChecking, aCard }));
        await using var dbB = PersistenceTestSupport.Create(f.ConnectionString, b.UserId);
        Assert.All(await dbB.Transactions.ToListAsync(), t => Assert.Contains(t.AccountId, new[] { bChecking, bCard }));
        Assert.Equal(9, await dbB.Transactions.IgnoreQueryFilters().CountAsync());
    }
}
