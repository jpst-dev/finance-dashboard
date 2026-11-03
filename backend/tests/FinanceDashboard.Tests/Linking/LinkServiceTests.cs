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
            bool deleted = false, bool manual = false,
            TransactionState state = TransactionState.PendingClassification, bool edited = false)
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
                State = state,
                Origin = manual ? TransactionOrigin.Manual : TransactionOrigin.Imported,
                BatchId = manual ? null : ids.Batch, IsDeleted = deleted, EditedByUser = edited,
            };
            db.Transactions.Add(t);
            await db.SaveChangesAsync();
            return t;
        }

        /// <summary>Itaú Pix line (Normal, as imported since S3(b)); <paramref name="name"/> is the memo fragment.</summary>
        public Task<Transaction> ItauPix(string name, decimal amount, int day) =>
            LineAsync(ImportSource.ItauChecking, $"PIX TRANSF {name}{day:00} 10", amount, day, state: TransactionState.Normal);

        /// <summary>Nubank Pix line (Normal) with the counterpart name in the usual memo layout.</summary>
        public Task<Transaction> NubankPix(string name, decimal amount, int day) =>
            LineAsync(ImportSource.NubankChecking,
                $"Transferência {(amount < 0 ? "enviada" : "recebida")} pelo Pix - {name} - ***.123.456-** - BANCO X",
                amount, day, state: TransactionState.Normal);

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

        Assert.Equal(new ReconcileResult(1, 0, 0), result);
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
        Assert.Equal(new ReconcileResult(0, 2, 0), first);
        Assert.Equal(2, await db.Links.CountAsync(l => l.State == LinkState.Suggested));

        await db.Transactions.Where(t => t.Id == i2.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        var second = await Service(db).ReconcileAsync();

        Assert.Equal(new ReconcileResult(1, 0, 0), second);
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

        Assert.Equal(new ReconcileResult(0, 0, 0), result);
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

        Assert.Equal(new ReconcileResult(0, 0, 0), await Service(a).ReconcileAsync());
        Assert.Equal(new ReconcileResult(0, 0, 0), await Service(b).ReconcileAsync());
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

        Assert.Equal(new ReconcileResult(0, 0, 0), await Service(db).ReconcileAsync());
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
        await Service(db).InvalidateAsync(i1.Id, InvalidateReason.Hidden);

        var link = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(LinkState.Confirmed, link.State);
        Assert.Equal((o.Id, i2.Id), (link.OutTransactionId, link.InTransactionId));
    }

    [Fact]
    public async Task Invalidate_keeps_rejected_links_so_the_pair_does_not_come_back()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.AccountPay(-100m, 5);
        var i = await w.CardPay(100m, 6);
        await Service(db).ReconcileAsync();
        var link = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(LinkOutcome.Ok, await Service(db).UndoAsync(link.Id));

        // The line is edited (date still inside the window): the rejection must survive.
        await db.Transactions.Where(t => t.Id == i.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Date, new DateOnly(2026, 10, 7)));
        await Service(db).InvalidateAsync(i.Id, InvalidateReason.ValueOrDate);

        var after = await db.Links.AsNoTracking().SingleAsync();
        Assert.Equal(LinkState.Rejected, after.State);
        Assert.Equal((o.Id, i.Id), (after.OutTransactionId, after.InTransactionId));
    }

    // ---- S3(b): reclassification ----

    private static Task<List<Link>> LinksOf(AppDbContext db, Guid line) =>
        db.Links.AsNoTracking().Where(l => l.OutTransactionId == line || l.InTransactionId == line).ToListAsync();

    private static async Task<TransactionState> StateOf(AppDbContext db, Guid id) =>
        (await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == id)).State;

    private static async Task<bool> CountsAsIncomeOrExpense(AppDbContext db, Guid id) =>
        await db.Transactions.AsNoTracking().WithoutConfirmedLink(db).AnyAsync(t => t.Id == id);

    [Fact]
    public async Task Reclassify_moves_unedited_pending_pix_and_boleto_to_normal()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var itauPix = await w.LineAsync(ImportSource.ItauChecking, "PIX TRANSF ALUGUEL10 10", -800m, 10);
        var boleto = await w.LineAsync(ImportSource.ItauChecking, "PAG BOLETO CONTA LUZ", -120m, 11);
        var nuPix = await w.LineAsync(ImportSource.NubankChecking,
            "Transferência enviada pelo Pix - LOJA FICTICIA - ***.123.456-** - BANCO X", -45m, 12);
        var invoice = await w.LineAsync(ImportSource.ItauChecking, "FATURA PAGA ITAU", -500m, 13);
        var pixCard = await w.LineAsync(ImportSource.ItauChecking, "PIX CARTAO LOJA20 10", -70m, 14);
        var nuInvoice = await w.AccountPay(-60m, 15);

        var result = await Service(db).ReconcileAsync();

        Assert.Equal(3, result.Reclassified);
        Assert.Equal(TransactionState.Normal, await StateOf(db, itauPix.Id));
        Assert.Equal(TransactionState.Normal, await StateOf(db, boleto.Id));
        Assert.Equal(TransactionState.Normal, await StateOf(db, nuPix.Id));
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, invoice.Id));
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, pixCard.Id));
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, nuInvoice.Id));
        Assert.False((await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == itauPix.Id)).EditedByUser);
    }

    [Fact]
    public async Task Reclassify_leaves_edited_and_manual_lines()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var edited = await w.LineAsync(ImportSource.ItauChecking, "PIX TRANSF ALUGUEL10 10", -800m, 10, edited: true);
        var manual = await w.LineAsync(ImportSource.ItauChecking, "PIX TRANSF ALUGUEL11 10", -800m, 11, manual: true);
        var hidden = await w.LineAsync(ImportSource.ItauChecking, "PAG BOLETO CONTA LUZ", -120m, 12, deleted: true);

        var result = await Service(db).ReconcileAsync();

        Assert.Equal(0, result.Reclassified);
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, edited.Id));
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, manual.Id));
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, hidden.Id));
    }

    [Fact]
    public async Task Reclassify_is_idempotent()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var pix = await w.LineAsync(ImportSource.ItauChecking, "PIX TRANSF ALUGUEL10 10", -800m, 10);
        await w.LineAsync(ImportSource.ItauChecking, "FATURA PAGA ITAU", -500m, 13);

        Assert.Equal(new ReconcileResult(0, 0, 1), await Service(db).ReconcileAsync());
        Assert.Equal(new ReconcileResult(0, 0, 0), await Service(db).ReconcileAsync());
        Assert.Equal(TransactionState.Normal, await StateOf(db, pix.Id));
    }

    // ---- S3(b): candidates in any state, name coherence, SQL prefilter ----

    [Fact]
    public async Task Normal_transfer_lines_with_accents_case_and_spaces_are_still_candidates()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        // Decomposed accent (FormD), upper case and runs of spaces: the SQL prefilter must still let them through.
        var o = await w.LineAsync(ImportSource.ItauChecking, "  pix   transf  MARIA L10 10", -300m, 10,
            state: TransactionState.Normal);
        var i = await w.LineAsync(ImportSource.NubankChecking,
            "TRANSFERÊNCIA RECEBIDA pelo Pix - MARIA LUISA - ***.123.456-** - BANCO X", 300m, 10,
            state: TransactionState.Normal);
        // A Normal line without a transfer role and the same amount is never a candidate.
        await w.LineAsync(ImportSource.NubankChecking, "Compra no débito - Loja", -300m, 10, state: TransactionState.Normal);

        await Service(db).ReconcileAsync();

        var link = Assert.Single(await db.Links.AsNoTracking().ToListAsync());
        Assert.Equal((o.Id, i.Id), (link.OutTransactionId, link.InTransactionId));
        Assert.Equal(LinkKind.OwnTransfer, link.Kind);
    }

    [Fact]
    public async Task Coherent_names_confirm_and_incoherent_names_only_suggest()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o1 = await w.ItauPix("MARIA L", -300m, 10);
        var i1 = await w.NubankPix("JOÃO PEDRO DA SILVA", 300m, 10);
        var o2 = await w.ItauPix("ALUGUEL", -1500m, 10);
        var i2 = await w.NubankPix("CLIENTE FICTICIO", 1500m, 10);

        Assert.Equal(new ReconcileResult(1, 1, 0), await Service(db).ReconcileAsync());

        Assert.Equal(LinkState.Confirmed, Assert.Single(await LinksOf(db, o1.Id)).State);
        var suggestion = Assert.Single(await LinksOf(db, o2.Id));
        Assert.Equal(LinkState.Suggested, suggestion.State);
        Assert.Equal(i2.Id, suggestion.InTransactionId);
        Assert.False(await CountsAsIncomeOrExpense(db, i1.Id));
        Assert.True(await CountsAsIncomeOrExpense(db, o2.Id));
        Assert.True(await CountsAsIncomeOrExpense(db, i2.Id));
    }

    // ---- S3(b): "é meu" (MarkOwn) and aliases ----

    [Fact]
    public async Task MarkOwn_creates_open_confirmed_link_and_alias()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.ItauPix("MARIA L", -300m, 10);
        var i = await w.NubankPix("JOSE LUIS DA SILVA", 200m, 20);

        var result = await Service(db).MarkOwnAsync(o.Id);

        Assert.Equal(LinkOutcome.Ok, result.Outcome);
        var link = await db.Links.AsNoTracking().SingleAsync(l => l.Id == result.LinkId);
        Assert.Equal((o.Id, (Guid?)null), (link.OutTransactionId, link.InTransactionId));
        Assert.Equal((LinkKind.OwnTransfer, LinkState.Confirmed, LinkOrigin.User), (link.Kind, link.State, link.Origin));
        Assert.NotNull(link.DecidedAt);
        Assert.Equal("MARIA L", Assert.Single(await db.OwnAliases.AsNoTracking().ToListAsync()).Fragment);
        Assert.False(await CountsAsIncomeOrExpense(db, o.Id));

        // Incoming line: the filled side follows the sign.
        var inResult = await Service(db).MarkOwnAsync(i.Id);
        Assert.Equal(LinkOutcome.Ok, inResult.Outcome);
        var inLink = await db.Links.AsNoTracking().SingleAsync(l => l.Id == inResult.LinkId);
        Assert.Equal(((Guid?)null, i.Id), (inLink.OutTransactionId, inLink.InTransactionId));
        Assert.Equal(2, await db.OwnAliases.CountAsync());

        Assert.Equal(LinkOutcome.Conflict, (await Service(db).MarkOwnAsync(o.Id)).Outcome);
        Assert.Equal(2, await db.OwnAliases.CountAsync());
    }

    [Fact]
    public async Task MarkOwn_with_short_name_links_without_alias()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.ItauPix("ANA", -300m, 10);

        var result = await Service(db).MarkOwnAsync(o.Id);

        Assert.Equal(LinkOutcome.Ok, result.Outcome);
        Assert.Equal(LinkState.Confirmed, Assert.Single(await LinksOf(db, o.Id)).State);
        Assert.Empty(await db.OwnAliases.ToListAsync());
    }

    [Fact]
    public async Task MarkOwn_requires_transfer_role()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var invoice = await w.AccountPay(-100m, 5);
        var boleto = await w.LineAsync(ImportSource.ItauChecking, "PAG BOLETO CONTA LUZ", -120m, 6, state: TransactionState.Normal);
        var manual = await w.LineAsync(ImportSource.ItauChecking, "PIX TRANSF MARIA L10 10", -10m, 10, manual: true);
        var hidden = await w.LineAsync(ImportSource.ItauChecking, "PIX TRANSF MARIA L11 10", -10m, 11, deleted: true);

        Assert.Equal(LinkOutcome.NotApplicable, (await Service(db).MarkOwnAsync(invoice.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotApplicable, (await Service(db).MarkOwnAsync(boleto.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotApplicable, (await Service(db).MarkOwnAsync(manual.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotFound, (await Service(db).MarkOwnAsync(hidden.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotFound, (await Service(db).MarkOwnAsync(Guid.NewGuid())).Outcome);
        Assert.Empty(await db.Links.ToListAsync());
    }

    [Fact]
    public async Task Alias_suggests_open_link_for_matching_line_never_confirms()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var marked = await w.ItauPix("MARIA L", -300m, 10);
        var other = await w.ItauPix("MaRia L", -150m, 20);
        var unrelated = await w.ItauPix("MARIA SO", -150m, 21);

        Assert.Equal(LinkOutcome.Ok, (await Service(db).MarkOwnAsync(marked.Id)).Outcome);

        var suggestion = Assert.Single(await LinksOf(db, other.Id));
        Assert.Equal((LinkState.Suggested, LinkOrigin.Auto, LinkKind.OwnTransfer),
            (suggestion.State, suggestion.Origin, suggestion.Kind));
        Assert.Equal((other.Id, (Guid?)null), (suggestion.OutTransactionId, suggestion.InTransactionId));
        Assert.Empty(await LinksOf(db, unrelated.Id));

        // Recomputed, not accumulated, and never confirmed by a reconcile.
        var again = await Service(db).ReconcileAsync();
        Assert.Equal(new ReconcileResult(0, 1, 0), again);
        var after = Assert.Single(await LinksOf(db, other.Id));
        Assert.Equal(LinkState.Suggested, after.State);
        Assert.True(await CountsAsIncomeOrExpense(db, other.Id));

        // Confirming the open suggestion works like any other.
        Assert.Equal(LinkOutcome.Ok, await Service(db).ConfirmAsync(after.Id));
        var confirmed = await db.Links.AsNoTracking().SingleAsync(l => l.Id == after.Id);
        Assert.Equal((LinkState.Confirmed, LinkOrigin.User), (confirmed.State, confirmed.Origin));
        Assert.False(await CountsAsIncomeOrExpense(db, other.Id));
    }

    [Fact]
    public async Task Alias_prefix_match_across_banks()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var itau = await w.ItauPix("MARIA L", -300m, 10);
        var nu = await w.NubankPix("João Pedro da Silva", 220m, 25); // different amount: no pair

        Assert.Equal(LinkOutcome.Ok, (await Service(db).MarkOwnAsync(itau.Id)).Outcome);

        var suggestion = Assert.Single(await LinksOf(db, nu.Id));
        Assert.Equal(LinkState.Suggested, suggestion.State);
        Assert.Equal(((Guid?)null, nu.Id), (suggestion.OutTransactionId, suggestion.InTransactionId));
    }

    [Fact]
    public async Task Rejected_open_link_blocks_alias_suggestion_for_that_line()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var marked = await w.ItauPix("MARIA L", -300m, 10);
        var refused = await w.ItauPix("MARIA L", -150m, 20);
        Assert.Equal(LinkOutcome.Ok, (await Service(db).MarkOwnAsync(marked.Id)).Outcome);
        var suggestion = Assert.Single(await LinksOf(db, refused.Id));

        Assert.Equal(LinkOutcome.Ok, await Service(db).RejectAsync(suggestion.Id));
        var later = await w.ItauPix("MARIA L", -90m, 22);
        await Service(db).ReconcileAsync();

        var kept = Assert.Single(await LinksOf(db, refused.Id));
        Assert.Equal(LinkState.Rejected, kept.State);
        Assert.Equal(LinkState.Suggested, Assert.Single(await LinksOf(db, later.Id)).State);
    }

    [Fact]
    public async Task Undo_open_link_marks_rejected()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var line = await w.ItauPix("MARIA L", -300m, 10);
        var mark = await Service(db).MarkOwnAsync(line.Id);

        Assert.Equal(LinkOutcome.Ok, await Service(db).UndoAsync(mark.LinkId!.Value));
        await Service(db).ReconcileAsync();

        // The alias stays, but the line is not suggested again (its own fragment matches the alias).
        var rejected = Assert.Single(await LinksOf(db, line.Id));
        Assert.Equal(LinkState.Rejected, rejected.State);
        Assert.True(await CountsAsIncomeOrExpense(db, line.Id));
        Assert.Single(await db.OwnAliases.ToListAsync());

        // Marking again replaces the rejection.
        var again = await Service(db).MarkOwnAsync(line.Id);
        Assert.Equal(LinkOutcome.Ok, again.Outcome);
        Assert.Equal(again.LinkId, Assert.Single(await LinksOf(db, line.Id)).Id);
    }

    // ---- S3(b): "sem contrapartida" ----

    [Fact]
    public async Task MarkNoCounterpart_creates_invoice_open_link()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var account = await w.AccountPay(-500m, 10);
        var card = await w.CardPay(700m, 12);

        var a = await Service(db).MarkNoCounterpartAsync(account.Id);
        var c = await Service(db).MarkNoCounterpartAsync(card.Id);

        Assert.Equal((LinkOutcome.Ok, LinkOutcome.Ok), (a.Outcome, c.Outcome));
        var la = await db.Links.AsNoTracking().SingleAsync(l => l.Id == a.LinkId);
        Assert.Equal((account.Id, (Guid?)null), (la.OutTransactionId, la.InTransactionId));
        Assert.Equal((LinkKind.InvoicePayment, LinkState.Confirmed, LinkOrigin.User), (la.Kind, la.State, la.Origin));
        var lc = await db.Links.AsNoTracking().SingleAsync(l => l.Id == c.LinkId);
        Assert.Equal(((Guid?)null, card.Id), (lc.OutTransactionId, lc.InTransactionId));
        Assert.Empty(await db.OwnAliases.ToListAsync());
        Assert.False(await CountsAsIncomeOrExpense(db, account.Id));
        Assert.Equal(LinkOutcome.Conflict, (await Service(db).MarkNoCounterpartAsync(account.Id)).Outcome);
    }

    [Fact]
    public async Task MarkNoCounterpart_requires_invoice_role()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var pix = await w.ItauPix("MARIA L", -300m, 10);
        var credit = await w.LineAsync(ImportSource.ItauChecking, "CREDITO LIBERAD PIX 1234", 200m, 10);

        Assert.Equal(LinkOutcome.NotApplicable, (await Service(db).MarkNoCounterpartAsync(pix.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotApplicable, (await Service(db).MarkNoCounterpartAsync(credit.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotFound, (await Service(db).MarkNoCounterpartAsync(Guid.NewGuid())).Outcome);
        Assert.Empty(await db.Links.ToListAsync());
    }

    [Fact]
    public async Task MarkNoCounterpart_requires_pending_state()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        // Invoice roles only pair while Pending: a Normal one means the user wants it counted.
        var counted = await w.LineAsync(ImportSource.NubankChecking, PayInvoice, -500m, 10,
            state: TransactionState.Normal, edited: true);

        Assert.Equal(LinkOutcome.NotApplicable, (await Service(db).MarkNoCounterpartAsync(counted.Id)).Outcome);
        Assert.Empty(await db.Links.ToListAsync());
    }

    [Fact]
    public async Task Alias_does_not_suggest_open_link_for_line_already_on_suggested_pair_in_same_run()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var marked = await w.ItauPix("MARIA L", -300m, 10);
        Assert.Equal(LinkOutcome.Ok, (await Service(db).MarkOwnAsync(marked.Id)).Outcome);
        // Matches the alias AND pairs (incoherent names ⇒ Suggested) with a line arriving in the same reconcile.
        var both = await w.ItauPix("MARIA L", -150m, 20);
        var other = await w.NubankPix("CLIENTE FICTICIO", 150m, 20);

        Assert.Equal(new ReconcileResult(0, 1, 0), await Service(db).ReconcileAsync());

        var only = Assert.Single(await LinksOf(db, both.Id));
        Assert.False(only.IsOpen);
        Assert.Equal((LinkState.Suggested, other.Id), (only.State, only.InTransactionId!.Value));
    }

    // ---- S3(b): Pix no crédito ----

    [Fact]
    public async Task PixCredit_line_set_to_normal_is_not_relinked()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var credit = await w.LineAsync(ImportSource.ItauChecking, "CREDITO LIBERAD PIX 1234", 200m, 10);
        var debit = await w.LineAsync(ImportSource.ItauChecking, "PIX CARTAO LOJA10 10", -200m, 10);
        Assert.Equal(new ReconcileResult(1, 0, 0), await Service(db).ReconcileAsync());
        Assert.Equal(LinkKind.PixCredit, (await db.Links.AsNoTracking().SingleAsync()).Kind);

        // The user says the credit counts: state → Normal (an edit), links dropped and never recreated.
        await db.Transactions.Where(t => t.Id == credit.Id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.State, TransactionState.Normal).SetProperty(t => t.EditedByUser, true));
        await Service(db).InvalidateAsync(credit.Id, InvalidateReason.StateToNormal);
        Assert.Equal(new ReconcileResult(0, 0, 0), await Service(db).ReconcileAsync());

        Assert.Empty(await db.Links.ToListAsync());
        Assert.Equal(TransactionState.PendingClassification, await StateOf(db, debit.Id));
        Assert.Equal(TransactionState.Normal, await StateOf(db, credit.Id));
    }

    // ---- S3(b): upgrade from open link to pair ----

    [Fact]
    public async Task Confirm_pair_removes_open_links_of_both_lines()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.ItauPix("ALUGUEL", -800m, 10);
        var i = await w.NubankPix("CLIENTE FICTICIO", 800m, 10);
        var markO = await Service(db).MarkOwnAsync(o.Id);
        var markI = await Service(db).MarkOwnAsync(i.Id);
        Assert.Equal((LinkOutcome.Ok, LinkOutcome.Ok), (markO.Outcome, markI.Outcome));
        // Incoherent names: the pair is only suggested, the open links stay.
        var pair = await db.Links.AsNoTracking().SingleAsync(l => l.OutTransactionId == o.Id && l.InTransactionId == i.Id);
        Assert.Equal(LinkState.Suggested, pair.State);
        Assert.Equal(3, await db.Links.CountAsync());

        Assert.Equal(LinkOutcome.Ok, await Service(db).ConfirmAsync(pair.Id));

        var only = Assert.Single(await db.Links.AsNoTracking().ToListAsync());
        Assert.Equal((pair.Id, LinkState.Confirmed), (only.Id, only.State));
    }

    [Fact]
    public async Task Reconcile_upgrades_open_link_when_coherent_counterpart_arrives()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.ItauPix("MARIA L", -300m, 10);
        Assert.Equal(LinkOutcome.Ok, (await Service(db).MarkOwnAsync(o.Id)).Outcome);
        var i = await w.NubankPix("JOSE LUIS DA SILVA", 300m, 10);

        Assert.Equal(new ReconcileResult(1, 0, 0), await Service(db).ReconcileAsync());

        var only = Assert.Single(await db.Links.AsNoTracking().ToListAsync());
        Assert.Equal((o.Id, i.Id, LinkState.Confirmed, LinkOrigin.Auto),
            (only.OutTransactionId!.Value, only.InTransactionId!.Value, only.State, only.Origin));
    }

    // ---- S3(b): invalidation reasons ----

    [Fact]
    public async Task Invalidate_value_edit_preserves_user_open_link()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.ItauPix("ALUGUEL", -800m, 10);
        var i = await w.NubankPix("CLIENTE FICTICIO", 800m, 10);
        var mark = await Service(db).MarkOwnAsync(o.Id);
        Assert.Equal(2, await db.Links.CountAsync()); // open Confirmed + suggested pair

        await db.Transactions.Where(t => t.Id == o.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Amount, -801m));
        await Service(db).InvalidateAsync(o.Id, InvalidateReason.ValueOrDate);

        var only = Assert.Single(await db.Links.AsNoTracking().ToListAsync());
        Assert.Equal(mark.LinkId, only.Id);
        Assert.Equal(LinkState.Confirmed, only.State);
        Assert.Empty(await LinksOf(db, i.Id));
    }

    [Fact]
    public async Task Invalidate_value_edit_drops_alias_suggestion_and_pair()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var marked = await w.ItauPix("MARIA L", -300m, 10);
        var other = await w.ItauPix("MARIA L", -150m, 20);
        await Service(db).MarkOwnAsync(marked.Id);
        Assert.Single(await LinksOf(db, other.Id));

        // The alias suggestion is automatic: it is dropped and then recomputed (same fragment ⇒ back again).
        await Service(db).InvalidateAsync(other.Id, InvalidateReason.ValueOrDate);
        Assert.Equal(LinkState.Suggested, Assert.Single(await LinksOf(db, other.Id)).State);
    }

    [Fact]
    public async Task Invalidate_hidden_removes_everything()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var o = await w.ItauPix("ALUGUEL", -800m, 10);
        var i = await w.NubankPix("CLIENTE FICTICIO", 800m, 10);
        await Service(db).MarkOwnAsync(o.Id);
        var pair = await db.Links.AsNoTracking().SingleAsync(l => l.InTransactionId == i.Id);
        Assert.Equal(LinkOutcome.Ok, await Service(db).RejectAsync(pair.Id));

        await db.Transactions.Where(t => t.Id == o.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true));
        await Service(db).InvalidateAsync(o.Id, InvalidateReason.Hidden);

        Assert.Empty(await db.Links.ToListAsync());
    }

    [Fact]
    public async Task Invalidate_state_to_normal_ignored_for_transfer_role()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var pix = await w.ItauPix("MARIA L", -300m, 10);
        var mark = await Service(db).MarkOwnAsync(pix.Id);

        await Service(db).InvalidateAsync(pix.Id, InvalidateReason.StateToNormal);

        Assert.Equal(mark.LinkId, Assert.Single(await LinksOf(db, pix.Id)).Id);
    }

    [Fact]
    public async Task Invalidate_state_to_normal_removes_invoice_role_links()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), "user-a");
        var w = new World(db);
        var pay = await w.AccountPay(-500m, 10);
        await Service(db).MarkNoCounterpartAsync(pay.Id);

        await db.Transactions.Where(t => t.Id == pay.Id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.State, TransactionState.Normal).SetProperty(t => t.EditedByUser, true));
        await Service(db).InvalidateAsync(pay.Id, InvalidateReason.StateToNormal);

        Assert.Empty(await db.Links.ToListAsync());
    }

    // ---- S3(b): concurrency and isolation ----

    [Fact]
    public async Task Concurrent_mark_own_creates_single_confirmed()
    {
        var cs = await NewDbAsync();
        Guid line;
        await using (var seed = PersistenceTestSupport.Create(cs, "user-a"))
            line = (await new World(seed).ItauPix("MARIA L", -300m, 10)).Id;

        await using var a = PersistenceTestSupport.Create(cs, "user-a");
        await using var b = PersistenceTestSupport.Create(cs, "user-a");
        var results = await Task.WhenAll(
            Task.Run(() => Service(a).MarkOwnAsync(line)),
            Task.Run(() => Service(b).MarkOwnAsync(line)));

        Assert.Equal(new[] { LinkOutcome.Ok, LinkOutcome.Conflict }, results.Select(r => r.Outcome).Order().ToArray());
        await using var check = PersistenceTestSupport.Create(cs, "user-a");
        Assert.Equal(LinkState.Confirmed, Assert.Single(await check.Links.ToListAsync()).State);
        Assert.Single(await check.OwnAliases.ToListAsync());
    }

    [Fact]
    public async Task Other_user_cannot_mark_foreign_line()
    {
        var cs = await NewDbAsync();
        await using var a = PersistenceTestSupport.Create(cs, "user-a");
        await using var b = PersistenceTestSupport.Create(cs, "user-b");
        var w = new World(a);
        var pix = await w.ItauPix("MARIA L", -300m, 10);
        var pay = await w.AccountPay(-500m, 10);

        Assert.Equal(LinkOutcome.NotFound, (await Service(b).MarkOwnAsync(pix.Id)).Outcome);
        Assert.Equal(LinkOutcome.NotFound, (await Service(b).MarkNoCounterpartAsync(pay.Id)).Outcome);

        Assert.Empty(await a.Links.ToListAsync());
        Assert.Empty(await a.OwnAliases.ToListAsync());
        Assert.Empty(await b.OwnAliases.ToListAsync());
    }
}
