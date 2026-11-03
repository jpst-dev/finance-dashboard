namespace FinanceDashboard.Domain;

public record SummaryLine(decimal Amount, AccountType AccountType, TransactionState State, Guid? CategoryId);

public record CategoryTotal(Guid? CategoryId, decimal Total);

public record SummaryResult(
    decimal Income,
    decimal Expenses,
    decimal Net,
    IReadOnlyList<CategoryTotal> ByCategory,
    int PendingCount,
    decimal PendingTotal);

public static class MonthlySummaryCalculator
{
    public static SummaryResult Compute(IEnumerable<SummaryLine> lines)
    {
        var all = lines.ToList();
        var pending = all.Where(l => l.State == TransactionState.PendingClassification).ToList();
        var normal = all.Where(l => l.State == TransactionState.Normal).ToList();

        // Income: positive on non-card accounts. Everything else (negatives anywhere,
        // positive card refunds) nets into Expenses.
        var income = normal.Where(l => l.Amount > 0 && l.AccountType != AccountType.CreditCard).Sum(l => l.Amount);
        var expenses = normal.Where(l => l.Amount < 0 || l.AccountType == AccountType.CreditCard).Sum(l => l.Amount);
        var byCategory = normal
            .GroupBy(l => l.CategoryId)
            .Select(g => new CategoryTotal(g.Key, g.Sum(l => l.Amount)))
            .ToList();

        return new SummaryResult(income, expenses, income + expenses, byCategory,
            pending.Count, pending.Sum(l => l.Amount));
    }
}
