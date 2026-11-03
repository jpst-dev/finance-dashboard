using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure;

public static class QueryExtensions
{
    /// <summary>Read-side view of transactions: hides soft-deleted rows (importer still sees them).</summary>
    public static IQueryable<Transaction> Visible(this IQueryable<Transaction> q) => q.Where(t => !t.IsDeleted);

    /// <summary>
    /// The one neutrality predicate: excludes lines (any state) on a Confirmed link with one or both sides present.
    /// Shared by the summary (income, expenses, categories, pending) and the pending list.
    /// </summary>
    public static IQueryable<Transaction> WithoutConfirmedLink(this IQueryable<Transaction> q, AppDbContext db) =>
        q.Where(t => !db.Links.Any(l => l.State == LinkState.Confirmed
            && (l.OutTransactionId == t.Id || l.InTransactionId == t.Id)));

    /// <summary>Excludes only transactions on a Confirmed link with both sides present (an open Confirmed link does not exclude).</summary>
    public static IQueryable<Transaction> WithoutPairedLink(this IQueryable<Transaction> q, AppDbContext db) =>
        q.Where(t => !db.Links.Any(l => l.State == LinkState.Confirmed
            && l.OutTransactionId != null && l.InTransactionId != null
            && (l.OutTransactionId == t.Id || l.InTransactionId == t.Id)));
}
