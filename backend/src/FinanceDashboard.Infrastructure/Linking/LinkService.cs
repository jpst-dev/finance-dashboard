using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceDashboard.Infrastructure.Linking;

public sealed record ReconcileResult(int Linked, int Suggested, int Reclassified);

public enum LinkOutcome { Ok, NotFound, Conflict, NotApplicable }

/// <summary>Outcome of a user mark; <see cref="LinkId"/> is the Confirmed link that now holds the line (when Ok).</summary>
public sealed record LinkResult(LinkOutcome Outcome, Guid? LinkId);

/// <summary>Why a line's links are being invalidated (spec S3(b), "InvalidateAsync").</summary>
public enum InvalidateReason
{
    /// <summary>Amount or date changed: drops pair links and automatic open suggestions, keeps the user's open marks.</summary>
    ValueOrDate,

    /// <summary>The line was hidden or deleted: drops every link that cites it, rejected ones included.</summary>
    Hidden,

    /// <summary>The state changed to Normal: drops the links unless the line is a transfer (any state pairs).</summary>
    StateToNormal,
}

/// <summary>
/// Reconciles invoice payments, own transfers and Pix-on-credit (pairing itself lives in <see cref="LinkMatcher"/>),
/// keeps open links (one side outside the imported accounts: "é meu", "sem contrapartida", alias suggestions) and
/// applies the user's decisions. Every operation runs under a per-user advisory lock inside a database transaction
/// (the ambient one when there is one), so reconciles and decisions of the same user never interleave.
/// </summary>
public sealed class LinkService(AppDbContext db, TimeProvider time)
{
    public Task<ReconcileResult> ReconcileAsync(CancellationToken ct = default) =>
        InLockedTransactionAsync(() => ReconcileCoreAsync(ct), ct);

    public Task<LinkOutcome> ConfirmAsync(Guid id, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => ConfirmCoreAsync(id, ct), ct);

    public Task<LinkOutcome> RejectAsync(Guid id, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => DecideAsync(id, LinkState.Suggested, LinkState.Rejected, ct), ct);

    /// <summary>Undoes a Confirmed link (pair or open) by marking it Rejected, so it is not proposed again.</summary>
    public Task<LinkOutcome> UndoAsync(Guid id, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => DecideAsync(id, LinkState.Confirmed, LinkState.Rejected, ct), ct);

    /// <summary>"É meu": the transfer's counterpart is one of the user's own accounts outside the imports.</summary>
    public Task<LinkResult> MarkOwnAsync(Guid transactionId, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => MarkCoreAsync(transactionId, LinkKind.OwnTransfer, ct), ct);

    /// <summary>"Sem contrapartida": the invoice payment's other side was not imported (and may never be).</summary>
    public Task<LinkResult> MarkNoCounterpartAsync(Guid transactionId, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => MarkCoreAsync(transactionId, LinkKind.InvoicePayment, ct), ct);

    /// <summary>
    /// Drops the links of an edited line according to <paramref name="reason"/> and reconciles again. Rejected links
    /// survive value/state edits (a rejected pair never comes back); hiding the line drops everything.
    /// </summary>
    public Task InvalidateAsync(Guid transactionId, InvalidateReason reason, CancellationToken ct = default) =>
        InLockedTransactionAsync(async () =>
        {
            var ofLine = db.Links.Where(l => l.OutTransactionId == transactionId || l.InTransactionId == transactionId);
            switch (reason)
            {
                case InvalidateReason.Hidden:
                    await ofLine.ExecuteDeleteAsync(ct);
                    break;
                case InvalidateReason.ValueOrDate:
                    // The user's open mark is about who the counterpart is, not about the amount: it stays.
                    await ofLine.Where(l => l.State != LinkState.Rejected
                            && !(l.State == LinkState.Confirmed && l.Origin == LinkOrigin.User
                                && (l.OutTransactionId == null || l.InTransactionId == null)))
                        .ExecuteDeleteAsync(ct);
                    break;
                case InvalidateReason.StateToNormal:
                    if (await RoleOfAsync(transactionId, ct) != PairingRole.Transfer)
                        await ofLine.Where(l => l.State != LinkState.Rejected).ExecuteDeleteAsync(ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
            }

            return await ReconcileCoreAsync(ct);
        }, ct);

    private sealed record Row(
        Guid Id, Guid AccountId, ImportSource Source, string OriginalDescription, DateOnly Date, decimal Amount,
        TransactionState State);

    private async Task<ReconcileResult> ReconcileCoreAsync(CancellationToken ct)
    {
        var reclassified = await ReclassifyAsync(ct);
        await DemoteIncoherentAutoTransfersAsync(ct);

        var candidates = new List<PairingCandidate>();
        foreach (var r in await CandidateRowsAsync(ct))
        {
            // Transfers are candidates in any state (Pix is Normal); the other roles only while Pending.
            if (PairingRules.RoleOf(r.Source, r.OriginalDescription, r.Amount) is { } role
                && (role == PairingRole.Transfer || r.State == TransactionState.PendingClassification))
                candidates.Add(new PairingCandidate(r.Id, r.AccountId, r.Source, role, r.Date, r.Amount,
                    role == PairingRole.Transfer ? PairingRules.CounterpartyKey(r.Source, r.OriginalDescription) : null));
        }

        // Suggestions (pair and open) are recomputed on every run; Confirmed and Rejected links stay.
        await db.Links.Where(l => l.State == LinkState.Suggested).ExecuteDeleteAsync(ct);

        var links = await db.Links.AsNoTracking().Where(l => l.State != LinkState.Suggested)
            .Select(l => new { l.State, l.OutTransactionId, l.InTransactionId }).ToListAsync(ct);
        var rejectedPairs = new HashSet<(Guid, Guid)>();
        var rejectedOpen = new HashSet<Guid>();
        var paired = new HashSet<Guid>();  // lines on a Confirmed pair link, either column
        var held = new HashSet<Guid>();    // lines on any live link (blocks alias suggestions)
        foreach (var l in links)
        {
            var open = l.OutTransactionId is null || l.InTransactionId is null;
            var ends = new[] { l.OutTransactionId, l.InTransactionId }.OfType<Guid>();
            if (l.State == LinkState.Rejected)
            {
                if (open) rejectedOpen.UnionWith(ends);
                else rejectedPairs.Add((l.OutTransactionId!.Value, l.InTransactionId!.Value));
                continue;
            }

            held.UnionWith(ends);
            if (!open) paired.UnionWith(ends);
        }

        var now = time.GetUtcNow();
        int linked = 0, suggested = 0;
        foreach (var p in LinkMatcher.Match(candidates, rejectedPairs))
        {
            // Guards a line already Confirmed on the other column (indexes are per column).
            if (paired.Contains(p.OutId) || paired.Contains(p.InId)) continue;
            held.Add(p.OutId);
            held.Add(p.InId);
            if (p.AutoConfirm)
            {
                // Upgrade: the open marks of both lines give way to the pair (the Confirmed indexes require it first).
                await OpenLinksOf(p.OutId, p.InId).ExecuteDeleteAsync(ct);
                paired.Add(p.OutId);
                paired.Add(p.InId);
                linked++;
            }
            else
            {
                suggested++;
            }

            db.Links.Add(new Link
            {
                Id = Guid.NewGuid(),
                Kind = p.Kind,
                State = p.AutoConfirm ? LinkState.Confirmed : LinkState.Suggested,
                OutTransactionId = p.OutId,
                InTransactionId = p.InId,
                Origin = LinkOrigin.Auto,
                CreatedAt = now,
                DecidedAt = p.AutoConfirm ? now : null,
            });
        }

        // Alias suggestions: a transfer with no live link whose counterpart fragment matches an alias of the user.
        // Pairing has priority (held), and a line whose open link the user refused is never suggested again.
        var aliases = await db.OwnAliases.AsNoTracking().Select(a => a.Fragment).ToListAsync(ct);
        if (aliases.Count > 0)
        {
            foreach (var c in candidates)
            {
                if (c.Role != PairingRole.Transfer || held.Contains(c.Id) || rejectedOpen.Contains(c.Id)) continue;
                if (!aliases.Any(a => NameKeys.Coincide(c.NameKey, a))) continue;
                db.Links.Add(OpenLink(c.Id, c.Amount, LinkKind.OwnTransfer, LinkState.Suggested, LinkOrigin.Auto, now));
                suggested++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new ReconcileResult(linked, suggested, reclassified);
    }

    /// <summary>
    /// Pairs the old S3(a) rule confirmed automatically without checking names (Confirmed, Origin=Auto, OwnTransfer)
    /// whose counterparty names do not coincide go back to the suggestion queue. The row is deleted and both lines
    /// become candidates again, so the normal flow recreates the pair as Suggested (no unique-index conflict, nothing
    /// to flip in place). User-origin links are never touched; once demoted nothing matches again, so it is idempotent.
    /// </summary>
    private async Task DemoteIncoherentAutoTransfersAsync(CancellationToken ct)
    {
        var pairs = await (
                from l in db.Links.AsNoTracking()
                where l.State == LinkState.Confirmed && l.Origin == LinkOrigin.Auto && l.Kind == LinkKind.OwnTransfer
                    && l.OutTransactionId != null && l.InTransactionId != null
                join o in db.Transactions.AsNoTracking() on l.OutTransactionId equals o.Id
                join ob in db.ImportBatches.AsNoTracking() on o.BatchId equals ob.Id
                join i in db.Transactions.AsNoTracking() on l.InTransactionId equals i.Id
                join ib in db.ImportBatches.AsNoTracking() on i.BatchId equals ib.Id
                select new
                {
                    l.Id,
                    OutSource = ob.Source, OutDescription = o.OriginalDescription,
                    InSource = ib.Source, InDescription = i.OriginalDescription,
                })
            .ToListAsync(ct);

        var ids = pairs
            .Where(p => !NameKeys.Coincide(PairingRules.CounterpartyKey(p.OutSource, p.OutDescription),
                PairingRules.CounterpartyKey(p.InSource, p.InDescription)))
            .Select(p => p.Id).ToList();
        if (ids.Count == 0) return;
        await db.Links.Where(l => ids.Contains(l.Id) && l.State == LinkState.Confirmed && l.Origin == LinkOrigin.Auto)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Imported, unedited, visible lines still Pending whose current classification rule says Normal become Normal
    /// (S3(b) moved Pix and boleto to Normal). Idempotent; manual and user-edited lines are never touched.
    /// </summary>
    private async Task<int> ReclassifyAsync(CancellationToken ct)
    {
        var pending = await (
                from t in db.Transactions.AsNoTracking().Visible()
                join b in db.ImportBatches.AsNoTracking() on t.BatchId equals b.Id
                where t.Origin == TransactionOrigin.Imported && !t.EditedByUser
                    && t.State == TransactionState.PendingClassification
                select new { t.Id, b.Source, t.OriginalDescription, t.Amount })
            .ToListAsync(ct);

        var ids = pending.Where(r => CurrentRule(r.Source, r.OriginalDescription, r.Amount) == TransactionState.Normal)
            .Select(r => r.Id).ToList();
        if (ids.Count == 0) return 0;
        return await db.Transactions.Where(t => ids.Contains(t.Id)
                && t.State == TransactionState.PendingClassification && !t.EditedByUser)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, TransactionState.Normal), ct);
    }

    private static TransactionState CurrentRule(ImportSource source, string description, decimal amount) => source switch
    {
        ImportSource.NubankChecking or ImportSource.NubankCard => NubankRules.Classify(source, description),
        ImportSource.ItauChecking => ItauRules.ClassifyChecking(description).State,
        ImportSource.ItauCard => ItauRules.ClassifyInvoice(description, amount),
        _ => TransactionState.PendingClassification,
    };

    /// <summary>
    /// Visible imported lines not on a Confirmed pair link: every Pending line, plus the Normal lines whose text can
    /// carry the transfer role (SQL prefilter from <see cref="PairingRules.TransferLikePatterns"/>, so the load does not
    /// grow with the whole Normal history). The exact role is decided in memory by <see cref="PairingRules.RoleOf"/>.
    /// </summary>
    private async Task<List<Row>> CandidateRowsAsync(CancellationToken ct)
    {
        var lines =
            from t in db.Transactions.AsNoTracking().Visible().WithoutPairedLink(db)
            join b in db.ImportBatches.AsNoTracking() on t.BatchId equals b.Id
            select new { t, b.Source };

        var rows = await lines.Where(x => x.t.State == TransactionState.PendingClassification)
            .Select(x => new Row(x.t.Id, x.t.AccountId, x.Source, x.t.OriginalDescription, x.t.Date, x.t.Amount, x.t.State))
            .ToListAsync(ct);

        foreach (var (source, patterns) in PairingRules.TransferLikePatterns)
        {
            rows.AddRange(await lines
                .Where(x => x.t.State == TransactionState.Normal && x.Source == source
                    && patterns.Any(p => EF.Functions.ILike(x.t.OriginalDescription, p)))
                .Select(x => new Row(x.t.Id, x.t.AccountId, x.Source, x.t.OriginalDescription, x.t.Date, x.t.Amount, x.t.State))
                .ToListAsync(ct));
        }

        return rows;
    }

    private async Task<LinkResult> MarkCoreAsync(Guid transactionId, LinkKind kind, CancellationToken ct)
    {
        var line = await (
                from t in db.Transactions.AsNoTracking().Visible()
                where t.Id == transactionId
                join b in db.ImportBatches.AsNoTracking() on t.BatchId equals b.Id into batches
                from b in batches.DefaultIfEmpty()
                select new { t.Id, t.Amount, t.OriginalDescription, t.State, Source = (ImportSource?)b.Source })
            .FirstOrDefaultAsync(ct);
        if (line is null) return new LinkResult(LinkOutcome.NotFound, null);

        var role = line.Source is { } source ? PairingRules.RoleOf(source, line.OriginalDescription, line.Amount) : null;
        var applicable = kind == LinkKind.OwnTransfer
            ? role == PairingRole.Transfer
            // Invoice roles only pair while Pending: a Normal invoice line means the user wants it counted.
            : role is PairingRole.InvoiceAccountSide or PairingRole.InvoiceCardSide
                && line.State == TransactionState.PendingClassification;
        if (!applicable) return new LinkResult(LinkOutcome.NotApplicable, null);

        if (await db.Links.AnyAsync(l => l.State == LinkState.Confirmed
                && (l.OutTransactionId == line.Id || l.InTransactionId == line.Id), ct))
            return new LinkResult(LinkOutcome.Conflict, null);

        // Live suggestions of the line and a previous refusal of an open link give way to the explicit mark.
        await db.Links.Where(l => (l.OutTransactionId == line.Id || l.InTransactionId == line.Id)
                && (l.State == LinkState.Suggested
                    || (l.State == LinkState.Rejected && (l.OutTransactionId == null || l.InTransactionId == null))))
            .ExecuteDeleteAsync(ct);

        var now = time.GetUtcNow();
        db.Links.Add(OpenLink(line.Id, line.Amount, kind, LinkState.Confirmed, LinkOrigin.User, now));

        if (kind == LinkKind.OwnTransfer
            && PairingRules.CounterpartyKey(line.Source!.Value, line.OriginalDescription) is { Length: >= NameKeys.MinLength and <= MaxFragment } key
            && !await db.OwnAliases.AnyAsync(a => a.Fragment == key, ct))
        {
            db.OwnAliases.Add(new OwnAlias { Id = Guid.NewGuid(), Fragment = key, CreatedAt = now });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return new LinkResult(LinkOutcome.Conflict, null);
        }

        // Other lines may now match the alias, and a counterpart already imported may upgrade the mark to a pair.
        await ReconcileCoreAsync(ct);
        var holder = await db.Links.AsNoTracking()
            .Where(l => l.State == LinkState.Confirmed && (l.OutTransactionId == line.Id || l.InTransactionId == line.Id))
            .Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        return new LinkResult(LinkOutcome.Ok, holder);
    }

    /// <summary>Same limit as the <c>OwnAliases.Fragment</c> column; longer names simply do not become aliases.</summary>
    private const int MaxFragment = 200;

    private static Link OpenLink(Guid line, decimal amount, LinkKind kind, LinkState state, LinkOrigin origin,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        State = state,
        // The filled side follows the sign: money out is the Out side, money in the In side.
        OutTransactionId = amount < 0 ? line : null,
        InTransactionId = amount < 0 ? null : line,
        Origin = origin,
        CreatedAt = now,
        DecidedAt = state == LinkState.Suggested ? null : now,
    };

    /// <summary>Live (Suggested/Confirmed) open links of the given lines.</summary>
    private IQueryable<Link> OpenLinksOf(Guid a, Guid b) =>
        db.Links.Where(l => l.State != LinkState.Rejected
            && ((l.InTransactionId == null && (l.OutTransactionId == a || l.OutTransactionId == b))
                || (l.OutTransactionId == null && (l.InTransactionId == a || l.InTransactionId == b))));

    private async Task<PairingRole?> RoleOfAsync(Guid transactionId, CancellationToken ct)
    {
        var line = await (
                from t in db.Transactions.AsNoTracking()
                where t.Id == transactionId
                join b in db.ImportBatches.AsNoTracking() on t.BatchId equals b.Id
                select new { b.Source, t.OriginalDescription, t.Amount })
            .FirstOrDefaultAsync(ct);
        return line is null ? null : PairingRules.RoleOf(line.Source, line.OriginalDescription, line.Amount);
    }

    private async Task<LinkOutcome> ConfirmCoreAsync(Guid id, CancellationToken ct)
    {
        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null) return LinkOutcome.NotFound;
        if (link.State != LinkState.Suggested) return LinkOutcome.Conflict;

        var outId = link.OutTransactionId;
        var inId = link.InTransactionId;
        if (link.IsOpen)
        {
            // An open suggestion conflicts with any Confirmed link of its line.
            var lineId = (outId ?? inId)!.Value;
            if (await db.Links.AnyAsync(l => l.State == LinkState.Confirmed
                    && (l.OutTransactionId == lineId || l.InTransactionId == lineId), ct))
                return LinkOutcome.Conflict;
        }
        else
        {
            // A pair conflicts only with another Confirmed pair; the open marks of both lines give way to it.
            var taken = await db.Links.AnyAsync(l => l.State == LinkState.Confirmed
                && l.OutTransactionId != null && l.InTransactionId != null
                && (l.OutTransactionId == outId || l.InTransactionId == outId
                    || l.OutTransactionId == inId || l.InTransactionId == inId), ct);
            if (taken) return LinkOutcome.Conflict;

            await OpenLinksOf(outId!.Value, inId!.Value).ExecuteDeleteAsync(ct);
            await db.Links.Where(l => l.Id != id && l.State == LinkState.Suggested
                    && (l.OutTransactionId == outId || l.InTransactionId == outId
                        || l.OutTransactionId == inId || l.InTransactionId == inId))
                .ExecuteDeleteAsync(ct);
        }

        link.State = LinkState.Confirmed;
        link.Origin = LinkOrigin.User;
        link.DecidedAt = time.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return LinkOutcome.Conflict;
        }

        return LinkOutcome.Ok;
    }

    private async Task<LinkOutcome> DecideAsync(Guid id, LinkState from, LinkState to, CancellationToken ct)
    {
        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null) return LinkOutcome.NotFound;
        if (link.State != from) return LinkOutcome.Conflict;

        link.State = to;
        link.DecidedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return LinkOutcome.Ok;
    }

    /// <summary>
    /// Runs <paramref name="body"/> holding the user's advisory transaction lock. The lock only lives inside a
    /// transaction: the ambient one is used when present (the caller commits), otherwise one is opened and committed here.
    /// </summary>
    private async Task<T> InLockedTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct)
    {
        var userId = db.CurrentUserId ?? throw new InvalidOperationException("No authenticated user.");
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(userId, ct);
            return await body();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(userId, ct);
        var result = await body();
        await tx.CommitAsync(ct);
        return result;
    }

    private Task<int> LockAsync(string userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId}, 0))", ct);
}
