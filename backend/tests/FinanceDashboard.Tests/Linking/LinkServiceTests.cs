using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Infrastructure.Linking;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.Linking;

[Collection(PostgresCollection.Name)]
public sealed class LinkServiceTests(PostgresFixture postgres)
{
    private const string PayInvoice = "Pagamento de fatura";
    private const string PayReceived = "Pagamento recebido";

    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var m = PersistenceTestSupport.Create(cs, null);
        await m.Database.MigrateAsync();
        return cs;
    }

    private static LinkService Service(AppDbContext db) => new(db, TimeProvider.System);

    /// <summary>Per-context seeding helper: one account and one confirmed batch per source.</summary>
    private sealed class World(AppDbContext db)
    {
        private readonly Dictionary<ImportSource, (Guid Account, Guid Batch)> _sources = [];

        public async Task<Transaction> LineAsync(ImportSource source, string description, decimal amount, int day,
            bool deleted = false, bool manual = false)
        {
            if (!_sources.TryGetValue(source, out var ids))
            {
                var isCard = source is ImportSource.NubankCard or ImportSource.ItauCard;
                var account = new Account
                {
                    Id = Guid.NewGuid(), Name = source.ToString(), Institution = "I",
                    Type = isCard ? AccountType.CreditCard : AccountType.Checking,
                };
                var batch = new ImportBatch
                {
                    Id = Guid.NewGuid(), AccountId = account.Id, Source = source, FileHash = Guid.NewGuid().ToString("N"),
                    State = ImportBatchState.Confirmed, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Accounts.Add(account);
                db.ImportBatches.Add(batch);
                await db.SaveChangesAsync();
                ids = (account.Id, batch.Id);
                _sources[source] = ids;
            }

            var t = new Transaction
            {
                Id = Guid.NewGuid(), AccountId = ids.Account, Date = new DateOnly(2026, 10, day), Amount = amount,
                OriginalDescription = description, NormalizedDescription = description,
                State = TransactionState.PendingClassification,
                Origin = manual ? TransactionOrigin.Manual : TransactionOrigin.Imported,
                BatchId = manual ? null : ids.Batch, IsDeleted = deleted,
            };
            db.Transactions.Add(t);
            await db.SaveChangesAsync();
            return t;
        }

        public Task<Transaction> AccountPay(decimal amount, int day) =>
            LineAsync(ImportSource.NubankChecking, PayInvoice, amount, day);

        public Task<Transaction> CardPay(decimal amount, int day) =>
            LineAsync(ImportSource.NubankCard, PayReceived, amount, day);
    }

    private static async Task<Link> AddLinkAsync(AppDbContext db, Guid outId, Guid inId, LinkState state)
    {
        var link = new Link
        {
            Id = Guid.NewGuid(), Kind = LinkKind.InvoicePayment, State = state, OutTransactionId = outId,
            InTransactionId = inId, Origin = LinkOrigin.Auto, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Links.Add(link);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return link;
    }

    [Fact]
    public async Task Reconcile_confirms_unambiguous_invoice_pair()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i = await w.CardPay(100m, 6);

        var result = await Service(db).ReconcileAsync();

        Assert.Equal(new ReconcileResult(1, 0), result);
        var link = await db.Links.SingleAsync();
        Assert.Equal(LinkState.Confirmed, link.State);
        Assert.Equal(LinkOrigin.Auto, link.Origin);
        Assert.Equal(LinkKind.InvoicePayment, link.Kind);
        Assert.Equal(o.Id, link.OutTransactionId);
        Assert.Equal(i.Id, link.InTransactionId);
        Assert.Equal("user-a", link.UserId);
    }

    [Fact]
    public async Task Reconcile_is_idempotent()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        await w.AccountPay(-100m, 5);
        await w.CardPay(100m, 6);
        await w.AccountPay(-50m, 5);
        await w.CardPay(50m, 6);
        await w.CardPay(50m, 7); // ambiguous: suggestions

        await Service(db).ReconcileAsync();
        var first = (await db.Links.AsNoTracking().ToListAsync())
            .Select(l => (l.OutTransactionId, l.InTransactionId, l.State)).Order().ToList();
        var again = await Service(db).ReconcileAsync();
        var second = (await db.Links.AsNoTracking().ToListAsync())
            .Select(l => (l.OutTransactionId, l.InTransactionId, l.State)).Order().ToList();

        Assert.Equal(first, second);
        Assert.Equal(0, again.Linked);
        Assert.Equal(2, again.Suggested);
    }

    [Fact]
    public async Task Reconcile_recomputes_suggestions_not_accumulating()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i1 = await w.CardPay(100m, 5);
        var i2 = await w.CardPay(100m, 6);

        var first = await Service(db).ReconcileAsync();
        Assert.Equal(new ReconcileResult(0, 2), first);
        Assert.Equal(2, await db.Links.CountAsync(l => l.State == LinkState.Suggested));

        await db.Transactions.Where(t => t.Id == i2.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        var second = await Service(db).ReconcileAsync();

        Assert.Equal(new ReconcileResult(1, 0), second);
        var link = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(LinkState.Confirmed, link.State);
        Assert.Equal((o.Id, i1.Id), (link.OutTransactionId, link.InTransactionId));
    }

    [Fact]
    public async Task Rejected_pair_is_not_recreated()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i1 = await w.CardPay(100m, 5);
        var i2 = await w.CardPay(100m, 6);
        await Service(db).ReconcileAsync();
        var toReject = await db.Links.AsNoTracking().SingleAsync(l => l.InTransactionId == i1.Id);

        Assert.Equal(LinkOutcome.Ok, await Service(db).RejectAsync(toReject.Id));
        var result = await Service(db).ReconcileAsync();

        Assert.Equal(1, result.Linked);
        var links = await db.Links.AsNoTracking().ToListAsync();
        Assert.Equal(2, links.Count);
        var rejected = Assert.Single(links, l => l.State == LinkState.Rejected);
        Assert.Equal((o.Id, i1.Id), (rejected.OutTransactionId, rejected.InTransactionId));
        Assert.NotNull(rejected.DecidedAt);
        var confirmed = Assert.Single(links, l => l.State == LinkState.Confirmed);
        Assert.Equal(i2.Id, confirmed.InTransactionId);
    }

    [Fact]
    public async Task Undo_marks_rejected_and_lines_return_to_pending()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i = await w.CardPay(100m, 6);
        await Service(db).ReconcileAsync();
        var link = await db.Links.AsNoTracking().SingleAsync();

        Assert.Equal(LinkOutcome.Ok, await Service(db).UndoAsync(link.Id));

        var after = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(LinkState.Rejected, after.State);
        Assert.NotNull(after.DecidedAt);
        var ids = (await db.Transactions.WithoutConfirmedLink(db).Select(t => t.Id).ToListAsync()).ToHashSet();
        Assert.Contains(o.Id, ids);
        Assert.Contains(i.Id, ids);
        Assert.Equal(LinkOutcome.Conflict, await Service(db).UndoAsync(link.Id));
        Assert.Equal(0, (await Service(db).ReconcileAsync()).Linked);
    }

    [Fact]
    public async Task Confirm_suggested_removes_competing_suggestions()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i1 = await w.CardPay(100m, 5);
        await w.CardPay(100m, 6);
        await Service(db).ReconcileAsync();
        var chosen = await db.Links.AsNoTracking().SingleAsync(l => l.InTransactionId == i1.Id);

        Assert.Equal(LinkOutcome.Ok, await Service(db).ConfirmAsync(chosen.Id));

        var link = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(chosen.Id, link.Id);
        Assert.Equal(LinkState.Confirmed, link.State);
        Assert.Equal(LinkOrigin.User, link.Origin);
        Assert.NotNull(link.DecidedAt);
        Assert.Equal(o.Id, link.OutTransactionId);
    }

    [Fact]
    public async Task Confirm_conflict_returns_Conflict()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i = await w.CardPay(100m, 6);
        var other = await w.CardPay(100m, 20);
        await Service(db).ReconcileAsync();
        var confirmed = await db.Links.AsNoTracking().SingleAsync();
        var competing = await AddLinkAsync(db, o.Id, other.Id, LinkState.Suggested);

        Assert.Equal(LinkOutcome.Conflict, await Service(db).ConfirmAsync(confirmed.Id)); // not Suggested
        Assert.Equal(LinkOutcome.Conflict, await Service(db).ConfirmAsync(competing.Id)); // out already confirmed
        Assert.Equal(LinkOutcome.NotFound, await Service(db).ConfirmAsync(Guid.NewGuid()));
        Assert.Equal(LinkOutcome.NotFound, await Service(db).RejectAsync(Guid.NewGuid()));
        Assert.Equal(LinkOutcome.Conflict, await Service(db).RejectAsync(confirmed.Id));
        Assert.Equal(i.Id, (await db.Links.AsNoTracking().SingleAsync(l => l.Id == confirmed.Id)).InTransactionId);
    }

    [Fact]
    public async Task Confirm_conflict_when_line_is_confirmed_on_the_other_column()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var a = await w.AccountPay(-100m, 5);
        var b = await w.CardPay(100m, 6);
        var c = await w.AccountPay(-100m, 7);
        await AddLinkAsync(db, a.Id, b.Id, LinkState.Confirmed);
        // b is In of the Confirmed link; here it would be Out. Indexes are per column, so only the service guards it.
        var cross = await AddLinkAsync(db, b.Id, c.Id, LinkState.Suggested);

        Assert.Equal(LinkOutcome.Conflict, await Service(db).ConfirmAsync(cross.Id));
        Assert.Equal(LinkState.Suggested, (await db.Links.AsNoTracking().SingleAsync(l => l.Id == cross.Id)).State);
    }

    [Fact]
    public async Task Reconcile_never_confirms_a_line_already_confirmed_on_the_other_column()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var p = await w.AccountPay(-100m, 5);   // pairable as Out with the card payment below
        var q = await w.AccountPay(-70m, 5);
        var card = await w.CardPay(100m, 6);
        // p already sits on the In column of a Confirmed link (abnormal data on purpose).
        await AddLinkAsync(db, q.Id, p.Id, LinkState.Confirmed);

        var result = await Service(db).ReconcileAsync();

        Assert.Equal(new ReconcileResult(0, 0), result);
        var links = await db.Links.AsNoTracking().ToListAsync();
        var only = Assert.Single(links);
        Assert.Equal((q.Id, p.Id), (only.OutTransactionId, only.InTransactionId));
        Assert.DoesNotContain(links, l => l.OutTransactionId == p.Id || l.InTransactionId == card.Id);
    }

    [Fact]
    public async Task Other_users_lines_never_pair()
    {
        var cs = await NewDbAsync();
        await using var a = PersistenceTestSupport.Create(cs, "user-a");
        await using var b = PersistenceTestSupport.Create(cs, "user-b");
        await new World(a).AccountPay(-100m, 5);
        await new World(b).CardPay(100m, 6);

        Assert.Equal(new ReconcileResult(0, 0), await Service(a).ReconcileAsync());
        Assert.Equal(new ReconcileResult(0, 0), await Service(b).ReconcileAsync());
        await using var raw = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Empty(await raw.Links.ToListAsync());
    }

    [Fact]
    public async Task Hidden_and_manual_lines_are_not_candidates()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        await w.AccountPay(-100m, 5);
        await w.LineAsync(ImportSource.NubankCard, PayReceived, 100m, 6, deleted: true);
        await w.LineAsync(ImportSource.NubankCard, PayReceived, 100m, 6, manual: true);

        Assert.Equal(new ReconcileResult(0, 0), await Service(db).ReconcileAsync());
        Assert.Empty(await db.Links.ToListAsync());
    }

    [Fact]
    public async Task Invalidate_removes_links_and_relinks_to_other_candidate()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i1 = await w.CardPay(100m, 6);
        await Service(db).ReconcileAsync();
        var i2 = await w.CardPay(100m, 7);
        Assert.Equal(i1.Id, (await db.Links.AsNoTracking().SingleAsync()).InTransactionId);

        // i1 changes (hidden): its link must go and o must relink to i2.
        await db.Transactions.Where(t => t.Id == i1.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        await Service(db).InvalidateAsync(i1.Id);

        var link = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(LinkState.Confirmed, link.State);
        Assert.Equal((o.Id, i2.Id), (link.OutTransactionId, link.InTransactionId));
    }
}
