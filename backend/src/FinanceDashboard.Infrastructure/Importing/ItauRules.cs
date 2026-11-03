using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

public static class ItauRules
{
    // Single list: prefixes of NormalizeText(memo). Known=true means the kind is recognized (not counted as unknown).
    private static readonly (string Prefix, TransactionState State)[] CheckingPrefixes =
    [
        ("FATURA PAGA", TransactionState.PendingClassification),
        ("PIX TRANSF", TransactionState.Normal),
        ("PIX CARTAO", TransactionState.PendingClassification),
        ("PAG BOLETO", TransactionState.Normal),
        ("CREDITO LIBERAD", TransactionState.PendingClassification),
        ("EST CRED", TransactionState.PendingClassification),
        ("CREDITO CONSIGNADO", TransactionState.PendingClassification),
        ("APLICACAO", TransactionState.PendingClassification),
        ("RESGATE", TransactionState.PendingClassification),
        ("PIX QRS", TransactionState.Normal),
        ("REND PAGO", TransactionState.Normal),
        ("REMUNERACAO SALARIO", TransactionState.Normal),
    ];

    public static (TransactionState State, bool Known) ClassifyChecking(string memo)
    {
        var normalized = SourceKeys.NormalizeText(memo);
        foreach (var (prefix, state) in CheckingPrefixes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return (state, true);
        return (TransactionState.Normal, false);
    }

    // ---- invoice ----

    private static readonly string[] InvoicePendingPrefixes = ["SALDO ANTERIOR", "SALDO ROTATIVO"];

    /// <summary>
    /// Pendente = payment of the previous invoice (first whole token <c>PAGAMENTO</c> and an amount that became
    /// positive in the normalized convention) or <c>SALDO ANTERIOR</c>/<c>SALDO ROTATIVO</c>; everything else
    /// (purchases, refunds, fees, IOF, interest, annual fee) = Normal.
    /// </summary>
    public static TransactionState ClassifyInvoice(string description, decimal normalizedAmount)
    {
        var normalized = SourceKeys.NormalizeText(description);
        var firstToken = normalized.Split(' ', 2)[0];
        if (firstToken == "PAGAMENTO" && normalizedAmount > 0) return TransactionState.PendingClassification;
        foreach (var prefix in InvoicePendingPrefixes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return TransactionState.PendingClassification;
        return TransactionState.Normal;
    }

    /// <summary>
    /// Installments after the first carry the original purchase date in the file; they are dated one month before
    /// the due date (clamped by <see cref="DateOnly.AddMonths"/>). Cash purchases and installment 1 keep the line date.
    /// </summary>
    public static DateOnly InvoiceLineDate(DateOnly purchaseDate, DateOnly dueDate, int? installmentNumber) =>
        installmentNumber > 1 ? dueDate.AddMonths(-1) : purchaseDate;

    /// <summary>Declared total = minus the sum of the normalized amounts of the lines that are not Pendente.</summary>
    public static bool InvoiceTotalMatches(decimal declaredTotal, IEnumerable<(decimal Amount, TransactionState State)> lines) =>
        declaredTotal == -lines.Where(l => l.State != TransactionState.PendingClassification).Sum(l => l.Amount);
}
