namespace FinanceDashboard.Domain;

public record LedgerLine(DateOnly Date, decimal Amount);

public static class BalanceCalculator
{
    public static BalanceCheckpoint? Latest(IEnumerable<BalanceCheckpoint> checkpoints) =>
        checkpoints.OrderByDescending(c => c.Date).ThenByDescending(c => c.CreatedAt).FirstOrDefault();

    public static decimal Compute(BalanceCheckpoint? latest, IEnumerable<LedgerLine> lines)
    {
        if (latest is null) return lines.Sum(l => l.Amount);
        return latest.Amount + lines.Where(l => l.Date > latest.Date).Sum(l => l.Amount);
    }
}
