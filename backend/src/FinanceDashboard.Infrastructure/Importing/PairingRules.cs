using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Maps a line's source and original description to its link-candidate role (spec: "Candidatas e tipos").</summary>
public static partial class PairingRules
{
    private const string ItauPixPrefix = "PIX TRANSF";

    [System.Text.RegularExpressions.GeneratedRegex(@"\s?\d{2}\s\d{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial System.Text.RegularExpressions.Regex ItauDateSuffix();

    /// <summary>
    /// Normalized counterparty name of a Pix transfer line (Itaú: fragment after the prefix without the trailing
    /// "DD MM"; Nubank: extracted name); <c>null</c> when the line is not a Pix transfer or has no name.
    /// </summary>
    public static string? CounterpartyKey(ImportSource source, string originalDescription)
    {
        switch (source)
        {
            case ImportSource.ItauChecking:
            {
                var n = SourceKeys.NormalizeText(originalDescription);
                if (!n.StartsWith(ItauPixPrefix, StringComparison.Ordinal)) return null;
                return NameKeys.Normalize(ItauDateSuffix().Replace(n[ItauPixPrefix.Length..], ""));
            }
            case ImportSource.NubankChecking:
            {
                if (!NubankRules.Normalize(originalDescription).TrimStart().StartsWith("transferencia ", StringComparison.Ordinal))
                    return null;
                return NameKeys.Normalize(NubankRules.ExtractCounterparty(originalDescription).Name);
            }
            default:
                return null;
        }
    }

    public static PairingRole? RoleOf(ImportSource source, string originalDescription, decimal amount)
    {
        switch (source)
        {
            case ImportSource.NubankChecking:
            {
                var n = NubankRules.Normalize(originalDescription).TrimStart();
                if (n.StartsWith("pagamento de fatura", StringComparison.Ordinal)) return PairingRole.InvoiceAccountSide;
                if (n.StartsWith("transferencia enviada", StringComparison.Ordinal)
                    || n.StartsWith("transferencia recebida", StringComparison.Ordinal)) return PairingRole.Transfer;
                return null;
            }
            case ImportSource.NubankCard:
                return NubankRules.Normalize(originalDescription).TrimStart()
                    .StartsWith("pagamento recebido", StringComparison.Ordinal)
                    ? PairingRole.InvoiceCardSide : null;
            case ImportSource.ItauChecking:
            {
                var n = SourceKeys.NormalizeText(originalDescription);
                if (n.StartsWith("FATURA PAGA", StringComparison.Ordinal)) return PairingRole.InvoiceAccountSide;
                if (n.StartsWith("PIX TRANSF", StringComparison.Ordinal)) return PairingRole.Transfer;
                if (n.StartsWith("CREDITO LIBERAD", StringComparison.Ordinal) && amount > 0) return PairingRole.PixCreditCredit;
                if ((n.StartsWith("PIX CARTAO", StringComparison.Ordinal) || n.StartsWith("EST CRED", StringComparison.Ordinal))
                    && amount < 0) return PairingRole.PixCreditDebit;
                return null;
            }
            case ImportSource.ItauCard:
                // Same rule as ItauRules.ClassifyInvoice: first whole token PAGAMENTO and positive amount.
                return SourceKeys.NormalizeText(originalDescription).Split(' ', 2)[0] == "PAGAMENTO" && amount > 0
                    ? PairingRole.InvoiceCardSide : null;
            default:
                return null;
        }
    }
}
