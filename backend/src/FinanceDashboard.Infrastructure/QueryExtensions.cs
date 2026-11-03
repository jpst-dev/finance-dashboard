using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure;

public static class QueryExtensions
{
    /// <summary>Read-side view of transactions: hides soft-deleted rows (importer still sees them).</summary>
    public static IQueryable<Transaction> Visible(this IQueryable<Transaction> q) => q.Where(t => !t.IsDeleted);
}
