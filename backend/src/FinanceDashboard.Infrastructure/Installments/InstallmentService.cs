using FinanceDashboard.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure.Installments;

/// <summary>
/// Reads the imported card installment lines and projects the committed future ones.
/// Manual lines, payments (positive) and lines without a valid "N de T" never define the base.
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

        var lines = rows.Select(r => new InstallmentLine(r.AccountId, r.AccountName, r.CardSuffix,
                r.NormalizedDescription, NameKeys.Normalize(r.OriginalDescription) ?? "", r.Number, r.Total, r.Amount,
                r.Statement))
            .ToList();

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        return InstallmentProjector.Project(lines, today);
    }
}
