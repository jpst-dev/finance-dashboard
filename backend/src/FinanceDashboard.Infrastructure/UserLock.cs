using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure;

/// <summary>
/// The per-user advisory transaction lock shared by the services that rewrite a user's lines in bulk (link
/// reconciliation and category rules), so their runs for the same user never interleave.
/// </summary>
internal static class UserLock
{
    /// <summary>
    /// Runs <paramref name="body"/> holding the user's advisory transaction lock. The lock only lives inside a
    /// transaction: the ambient one is used when present (the caller commits), otherwise one is opened and committed
    /// here. Reentrant: taking it again in the same transaction does not block.
    /// </summary>
    public static async Task<T> InLockedTransactionAsync<T>(AppDbContext db, Func<Task<T>> body, CancellationToken ct)
    {
        var userId = db.CurrentUserId ?? throw new InvalidOperationException("No authenticated user.");
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(db, userId, ct);
            return await body();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, userId, ct);
        var result = await body();
        await tx.CommitAsync(ct);
        return result;
    }

    private static Task<int> LockAsync(AppDbContext db, string userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId}, 0))", ct);
}
