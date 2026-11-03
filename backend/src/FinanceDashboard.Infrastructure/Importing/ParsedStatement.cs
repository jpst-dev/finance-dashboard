using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>
/// One line as the reader produced it. Its identity is (<see cref="SourceKey"/>, <see cref="Ordinal"/>): the FITID
/// with ordinal 0 for Nubank, a content key with its k-th occurrence in the file for Itaú. The reader also decides the
/// initial <see cref="State"/> and the counterparty.
/// </summary>
public sealed record ParsedLine(string SourceKey, int Ordinal, DateOnly Date, decimal Amount, string Memo, TransactionState State)
{
    public string? CounterpartyName { get; init; }
    public string? CounterpartyDocument { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public DateOnly? StatementDate { get; init; }
    public int? InstallmentNumber { get; init; }
    public int? InstallmentTotal { get; init; }
    public string? CardSuffix { get; init; }
    public CardKind? CardKind { get; init; }
}

public sealed record ParsedBalance(decimal Amount, DateOnly AsOf);

public sealed record ParsedInvoice(DateOnly DueDate, decimal DeclaredTotal);

/// <param name="LedgerBalance">Checkpoint candidate (null when the reader decided it must not become one).</param>
public sealed record ParsedStatement(
    ImportSource Source,
    string? AccountExternalId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    IReadOnlyList<ParsedLine> Lines,
    ParsedBalance? LedgerBalance)
{
    /// <summary>The LEDGERBAL read from the file, kept on the batch even when it is not a checkpoint.</summary>
    public ParsedBalance? LedgerRead { get; init; }

    public ParsedInvoice? Invoice { get; init; }

    /// <summary>Warnings the reader can decide alone (no database).</summary>
    public IReadOnlyList<ImportWarning> Warnings { get; init; } = [];
}
