using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Maps a line's source and original description to its link-candidate role (spec: "Candidatas e tipos").</summary>
public static class PairingRules
{
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
