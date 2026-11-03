using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

public static class ItauRules
{
    // Single list: prefixes of NormalizeText(memo). Known=true means the kind is recognized (not counted as unknown).
    private static readonly (string Prefix, TransactionState State)[] CheckingPrefixes =
    [
        ("FATURA PAGA", TransactionState.PendingClassification),
        ("PIX TRANSF", TransactionState.PendingClassification),
        ("PIX CARTAO", TransactionState.PendingClassification),
        ("PAG BOLETO", TransactionState.PendingClassification),
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
}
