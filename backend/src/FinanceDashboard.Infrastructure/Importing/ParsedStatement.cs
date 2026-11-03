using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

public record ParsedLine(string FitId, DateOnly Date, decimal Amount, string Memo);

public record ParsedBalance(decimal Amount, DateOnly AsOf);

public record ParsedStatement(
    ImportSource Source,
    string? AccountExternalId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    IReadOnlyList<ParsedLine> Lines,
    ParsedBalance? LedgerBalance);
