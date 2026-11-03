using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure;

public static class QueryExtensions
{
    /// <summary>Read-side view of transactions: hides soft-deleted rows (importer still sees them).</summary>
    public static IQueryable<Transaction> Visible(this IQueryable<Transaction> q) => q.Where(t => !t.IsDeleted);

    /// <summary>Excludes transactions already on either side of a confirmed link.</summary>
    public static IQueryable<Transaction> WithoutConfirmedLink(this IQueryable<Transaction> q, AppDbContext db) =>
        q.Where(t => !db.Links.Any(l => l.State == LinkState.Confirmed
            && (l.OutTransactionId == t.Id || l.InTransactionId == t.Id)));
}
