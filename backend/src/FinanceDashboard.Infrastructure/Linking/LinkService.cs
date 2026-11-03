using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceDashboard.Infrastructure.Linking;

public sealed record ReconcileResult(int Linked, int Suggested);

public enum LinkOutcome { Ok, NotFound, Conflict }

/// <summary>
/// Reconciles invoice payments and own transfers (pairing itself lives in <see cref="LinkMatcher"/>) and applies the
/// user's decisions. Every operation runs under a per-user advisory lock inside a database transaction (the ambient
/// one when there is one), so reconciles and decisions of the same user never interleave.
/// </summary>
public sealed class LinkService(AppDbContext db, TimeProvider time)
{
    public Task<ReconcileResult> ReconcileAsync(CancellationToken ct = default) =>
        InLockedTransactionAsync(() => ReconcileCoreAsync(ct), ct);

    public Task<LinkOutcome> ConfirmAsync(Guid id, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => ConfirmCoreAsync(id, ct), ct);

    public Task<LinkOutcome> RejectAsync(Guid id, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => DecideAsync(id, LinkState.Suggested, LinkState.Rejected, ct), ct);

    public Task<LinkOutcome> UndoAsync(Guid id, CancellationToken ct = default) =>
        InLockedTransactionAsync(() => DecideAsync(id, LinkState.Confirmed, LinkState.Rejected, ct), ct);

    /// <summary>Drops the Suggested and Confirmed links that cite the line (Rejected ones survive, so a rejected pair never
    /// comes back after an edit) and reconciles again.</summary>
    public Task InvalidateAsync(Guid transactionId, CancellationToken ct = default) =>
        InLockedTransactionAsync(async () =>
        {
            await db.Links.Where(l => l.State != LinkState.Rejected
                    && (l.OutTransactionId == transactionId || l.InTransactionId == transactionId))
                .ExecuteDeleteAsync(ct);
            return await ReconcileCoreAsync(ct);
        }, ct);

    private async Task<ReconcileResult> ReconcileCoreAsync(CancellationToken ct)
    {
        var rows = await (
                from t in db.Transactions.AsNoTracking().Visible().WithoutConfirmedLink(db)
                join b in db.ImportBatches.AsNoTracking() on t.BatchId equals b.Id
                where t.State == TransactionState.PendingClassification
                    || t.State == TransactionState.Normal
                select new { t.Id, t.AccountId, b.Source, t.OriginalDescription, t.Date, t.Amount, t.State })
            .ToListAsync(ct);

        var candidates = new List<PairingCandidate>();
        foreach (var r in rows)
        {
            // Transfers are candidates in any state (Pix is Normal now); the other roles only while Pending.
            if (PairingRules.RoleOf(r.Source, r.OriginalDescription, r.Amount) is { } role
                && (role == PairingRole.Transfer || r.State == TransactionState.PendingClassification))
                // TEMPORARY (S3(b) Task 1): real counterpart name keys arrive with Task 4. A shared constant keeps
                // the S3(a) auto-confirm behavior of own transfers until then.
                candidates.Add(new PairingCandidate(r.Id, r.AccountId, r.Source, role, r.Date, r.Amount,
                    role == PairingRole.Transfer ? "TRANSFERENCIA-PROPRIA" : null));
        }

        var rejected = (await db.Links.AsNoTracking().Where(l => l.State == LinkState.Rejected)
                .Select(l => new { l.OutTransactionId, l.InTransactionId }).ToListAsync(ct))
            .Where(l => l.OutTransactionId.HasValue && l.InTransactionId.HasValue)
            .Select(l => (l.OutTransactionId!.Value, l.InTransactionId!.Value)).ToHashSet();

        // Lines on a Confirmed link, either column (the indexes only cover each column on its own).
        var confirmedLines = await ConfirmedLinesAsync(ct);

        await db.Links.Where(l => l.State == LinkState.Suggested).ExecuteDeleteAsync(ct);

        var now = time.GetUtcNow();
        int linked = 0, suggested = 0;
        foreach (var p in LinkMatcher.Match(candidates, rejected))
        {
            if (confirmedLines.Contains(p.OutId) || confirmedLines.Contains(p.InId)) continue;
            var confirm = p.AutoConfirm;
            if (confirm)
            {
                confirmedLines.Add(p.OutId);
                confirmedLines.Add(p.InId);
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
                State = confirm ? LinkState.Confirmed : LinkState.Suggested,
                OutTransactionId = p.OutId,
                InTransactionId = p.InId,
                Origin = LinkOrigin.Auto,
                CreatedAt = now,
                DecidedAt = confirm ? now : null,
            });
        }

        await db.SaveChangesAsync(ct);
        return new ReconcileResult(linked, suggested);
    }

    private async Task<LinkOutcome> ConfirmCoreAsync(Guid id, CancellationToken ct)
    {
        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null) return LinkOutcome.NotFound;
        if (link.State != LinkState.Suggested) return LinkOutcome.Conflict;

        var outId = link.OutTransactionId;
        var inId = link.InTransactionId;
        var taken = await db.Links.AnyAsync(l => l.State == LinkState.Confirmed
            && (l.OutTransactionId == outId || l.InTransactionId == outId
                || l.OutTransactionId == inId || l.InTransactionId == inId), ct);
        if (taken) return LinkOutcome.Conflict;

        await db.Links.Where(l => l.Id != id && l.State == LinkState.Suggested
                && (l.OutTransactionId == outId || l.InTransactionId == outId
                    || l.OutTransactionId == inId || l.InTransactionId == inId))
            .ExecuteDeleteAsync(ct);

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

    private async Task<HashSet<Guid>> ConfirmedLinesAsync(CancellationToken ct)
    {
        var ends = await db.Links.AsNoTracking().Where(l => l.State == LinkState.Confirmed)
            .Select(l => new { l.OutTransactionId, l.InTransactionId }).ToListAsync(ct);
        var set = new HashSet<Guid>();
        foreach (var e in ends)
        {
            if (e.OutTransactionId is { } o) set.Add(o);
            if (e.InTransactionId is { } i) set.Add(i);
        }

        return set;
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
