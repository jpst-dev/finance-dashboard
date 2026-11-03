using FinanceDashboard.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure.Installments;

/// <summary>
/// Reads the imported card installment lines and projects the committed future ones.
/// The base of an account is its newest invoice (max StatementDate over imported, visible, negative lines of
/// credit-card accounts), whether or not that invoice has installments. Manual lines, payments (positive) and lines
/// without a valid "N de T" never define the base nor become installments.
/// </summary>
public sealed class InstallmentService(AppDbContext db, TimeProvider time)
{
    public async Task<InstallmentProjection> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.Transactions.AsNoTracking().Visible()
            .Where(t => t.Origin == TransactionOrigin.Imported && t.StatementDate != null && t.Amount < 0
                && t.InstallmentNumber != null && t.InstallmentTotal != null
                && t.InstallmentTotal >= 2 && t.InstallmentNumber >= 1 && t.InstallmentNumber <= t.InstallmentTotal)
            .Join(db.Accounts.Where(a => a.Type == AccountType.CreditCard), t => t.AccountId, a => a.Id,
                (t, a) => new
                {
                    t.AccountId, AccountName = a.Name, t.CardSuffix, t.NormalizedDescription, t.OriginalDescription,
                    Number = t.InstallmentNumber!.Value, Total = t.InstallmentTotal!.Value, t.Amount,
                    Statement = t.StatementDate!.Value,
                })
            .ToListAsync(ct);

        var baseRows = await db.Transactions.AsNoTracking().Visible()
            .Where(t => t.Origin == TransactionOrigin.Imported && t.StatementDate != null && t.Amount < 0)
            .Join(db.Accounts.Where(a => a.Type == AccountType.CreditCard), t => t.AccountId, a => a.Id,
                (t, _) => new { t.AccountId, Statement = t.StatementDate!.Value })
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Base = g.Max(x => x.Statement) })
            .ToListAsync(ct);
        var bases = baseRows.ToDictionary(x => x.AccountId, x => x.Base);

        var lines = rows.Select(r => new InstallmentLine(r.AccountId, r.AccountName, r.CardSuffix,
                r.NormalizedDescription, NameKeys.Normalize(r.OriginalDescription) ?? "", r.Number, r.Total, r.Amount,
                r.Statement))
            .ToList();

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        return InstallmentProjector.Project(lines, today, bases);
    }
}
