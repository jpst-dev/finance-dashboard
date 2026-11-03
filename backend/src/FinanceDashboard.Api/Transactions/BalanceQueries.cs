using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Transactions;

public static class BalanceQueries
{
    /// <summary>Balance per account: latest checkpoint plus the visible lines after it (rule lives in the Domain).</summary>
    public static async Task<Dictionary<Guid, decimal>> BalancesAsync(
        AppDbContext db, IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
    {
        var checkpoints = (await db.Checkpoints.AsNoTracking()
                .Where(c => accountIds.Contains(c.AccountId)).ToListAsync(ct))
            .ToLookup(c => c.AccountId);

        var result = new Dictionary<Guid, decimal>();
        foreach (var id in accountIds)
        {
            var latest = BalanceCalculator.Latest(checkpoints[id]);
            var query = db.Transactions.AsNoTracking().Visible().Where(t => t.AccountId == id);
            if (latest is not null)
            {
                var after = latest.Date;
                query = query.Where(t => t.Date > after);
            }

            var lines = await query.Select(t => new LedgerLine(t.Date, t.Amount)).ToListAsync(ct);
            result[id] = BalanceCalculator.Compute(latest, lines);
        }

        return result;
    }
}
