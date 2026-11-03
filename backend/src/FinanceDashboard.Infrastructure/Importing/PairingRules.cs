using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Maps a line's source and original description to its link-candidate role (spec: "Candidatas e tipos").</summary>
public static partial class PairingRules
{
    // Transfer prefixes, in the normalized form each source's rule compares against (Itaú: SourceKeys.NormalizeText;
    // Nubank: NubankRules.Normalize, lower case). RoleOf and the SQL prefilter below both read these lists.
    private const string ItauPixPrefix = "PIX TRANSF";
    private static readonly string[] NubankTransferPrefixes = ["transferencia enviada", "transferencia recebida"];

    /// <summary>
    /// ILIKE patterns that every description with the <see cref="PairingRole.Transfer"/> role matches, per source
    /// (a superset: other lines may match too). Lets the reconciliation load Normal lines from SQL only when they can
    /// be transfers, instead of the whole history. Derived from the prefixes above by <see cref="LikeSuperset"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<ImportSource, string[]> TransferLikePatterns =
        new Dictionary<ImportSource, string[]>
        {
            [ImportSource.ItauChecking] = [LikeSuperset(ItauPixPrefix)],
            [ImportSource.NubankChecking] = NubankTransferPrefixes.Select(LikeSuperset).ToArray(),
        };

    /// <summary>
    /// Turns a normalized prefix into an ILIKE pattern that matches the raw text whatever its case, accents (composed
    /// or decomposed) and whitespace runs: letters that may carry a diacritic and spaces become <c>%</c>, the rest is
    /// kept; leading <c>%</c> for leading whitespace. Prefixes hold only letters and spaces, so nothing needs escaping.
    /// </summary>
    private static string LikeSuperset(string normalizedPrefix)
    {
        var sb = new System.Text.StringBuilder("%");
        foreach (var ch in normalizedPrefix)
        {
            if (!char.IsAsciiLetter(ch) && ch != ' ')
                throw new ArgumentException($"Unsupported character in prefix '{normalizedPrefix}'.", nameof(normalizedPrefix));
            var wild = ch == ' ' || "AEIOUCNY".Contains(char.ToUpperInvariant(ch));
            if (!wild) sb.Append(ch);
            else if (sb[^1] != '%') sb.Append('%');
        }

        if (sb[^1] != '%') sb.Append('%');
        return sb.ToString();
    }

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
                if (RoleOf(source, originalDescription, 0m) != PairingRole.Transfer) return null;
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
                if (NubankTransferPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal))) return PairingRole.Transfer;
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
                if (n.StartsWith(ItauPixPrefix, StringComparison.Ordinal)) return PairingRole.Transfer;
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
