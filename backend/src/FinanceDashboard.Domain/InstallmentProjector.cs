namespace FinanceDashboard.Domain;

public sealed record InstallmentLine(Guid AccountId, string AccountName, string? CardSuffix, string Description,
    string NormalizedDescription, int Number, int Total, decimal Amount, DateOnly StatementDate);

public sealed record AccountBase(Guid AccountId, string AccountName, DateOnly BasedOn);

public sealed record InstallmentItem(Guid AccountId, string Description, string? CardSuffix, int InstallmentNumber,
    int InstallmentTotal, decimal Amount);

public sealed record InstallmentMonth(DateOnly Month, decimal Total, IReadOnlyList<InstallmentItem> Items);

public sealed record InstallmentGroupView(Guid AccountId, string AccountName, string? CardSuffix, string Description,
    int InstallmentNumber, int InstallmentTotal, int Remaining, decimal Amount, int Multiplicity, DateOnly EndsOn,
    decimal RemainingTotal);

public sealed record InstallmentProjection(decimal TotalCommitted, IReadOnlyList<AccountBase> Accounts,
    IReadOnlyList<InstallmentMonth> Months, IReadOnlyList<InstallmentGroupView> Groups);

public static class InstallmentProjector
{
    public const int MaxInstallments = 36;

    private sealed record Projected(InstallmentLine Line, decimal Amount, DateOnly Date);

    public static InstallmentProjection Project(IReadOnlyList<InstallmentLine> lines, DateOnly today)
    {
        var cut = new DateOnly(today.Year, today.Month, 1);
        var valid = lines.Where(l => l.Amount < 0 && l.Total >= 2 && l.Number >= 1 && l.Number <= l.Total).ToList();

        var bases = valid
            .GroupBy(l => l.AccountId)
            .Select(g => new { g.Key, Base = g.Max(l => l.StatementDate), Name = g.Select(l => l.AccountName).OrderBy(n => n, StringComparer.Ordinal).First() })
            .ToDictionary(x => x.Key);

        var accounts = bases.Values
            .Select(x => new AccountBase(x.Key, x.Name, x.Base))
            .OrderBy(a => a.AccountName, StringComparer.Ordinal).ThenBy(a => a.AccountId)
            .ToList();

        // Every installment is computed from the base date, never chained (day 31 must not drift to 28).
        var baseLines = valid.Where(l => l.StatementDate == bases[l.AccountId].Base).ToList();
        var projected = new List<List<Projected>>();
        var all = new List<Projected>();
        foreach (var l in baseLines)
        {
            var list = new List<Projected>();
            var last = Math.Min(l.Total - l.Number, MaxInstallments);
            for (var i = 1; i <= last; i++)
            {
                var date = l.StatementDate.AddMonths(i - 1);
                if (date < cut) continue;
                list.Add(new Projected(l, Math.Abs(l.Amount), date));
            }
            projected.Add(list);
            all.AddRange(list);
        }

        var months = all
            .GroupBy(p => new DateOnly(p.Date.Year, p.Date.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var items = g
                    .OrderBy(p => p.Line.AccountName, StringComparer.Ordinal).ThenBy(p => p.Line.AccountId)
                    .ThenBy(p => p.Line.Description, StringComparer.Ordinal)
                    .ThenBy(p => p.Line.CardSuffix ?? "", StringComparer.Ordinal)
                    .ThenBy(p => p.Line.Number).ThenBy(p => p.Line.Total).ThenBy(p => p.Amount)
                    .Select(p => new InstallmentItem(p.Line.AccountId, p.Line.Description, p.Line.CardSuffix,
                        p.Line.Number, p.Line.Total, p.Amount))
                    .ToList();
                return new InstallmentMonth(g.Key, items.Sum(i => i.Amount), items);
            })
            .ToList();

        var groups = baseLines.Select((l, idx) => (Line: l, Idx: idx))
            .GroupBy(x => (x.Line.AccountId, x.Line.NormalizedDescription, x.Line.Number, x.Line.Total, Amount: Math.Abs(x.Line.Amount)))
            .Select(g =>
            {
                var members = g.Select(x => x.Line).ToList();
                var remaining = projected[g.First().Idx].Count;
                if (remaining == 0) return null;
                var first = members
                    .OrderBy(l => l.Description, StringComparer.Ordinal)
                    .ThenBy(l => l.CardSuffix ?? "", StringComparer.Ordinal).First();
                var ends = g.SelectMany(x => projected[x.Idx]).Max(p => p.Date);
                return new InstallmentGroupView(g.Key.AccountId, first.AccountName, first.CardSuffix, first.Description,
                    g.Key.Number, g.Key.Total, remaining, g.Key.Amount, members.Count, ends,
                    g.Key.Amount * members.Count * remaining);
            })
            .Where(v => v is not null).Select(v => v!)
            .OrderBy(v => v.EndsOn).ThenBy(v => v.Description, StringComparer.Ordinal)
            .ThenBy(v => v.AccountName, StringComparer.Ordinal).ThenBy(v => v.AccountId)
            .ThenBy(v => v.InstallmentNumber).ThenBy(v => v.InstallmentTotal).ThenBy(v => v.Amount)
            .ToList();

        return new InstallmentProjection(months.Sum(m => m.Total), accounts, months, groups);
    }
}
