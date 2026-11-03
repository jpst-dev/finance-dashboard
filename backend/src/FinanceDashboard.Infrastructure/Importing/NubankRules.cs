using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

public static partial class NubankRules
{
    private static readonly string[] PendingPrefixes =
    [
        "pagamento de fatura",
        "pagamento recebido",
        "transferencia enviada",
        "transferencia recebida",
        "pix no credito",
    ];

    public static TransactionState Classify(ImportSource source, string memo)
    {
        var normalized = Normalize(memo).TrimStart();
        foreach (var prefix in PendingPrefixes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return TransactionState.PendingClassification;
        return TransactionState.Normal;
    }

    public static (string? Name, string? MaskedDocument) ExtractCounterparty(string memo)
    {
        var m = PixRegex().Match(memo.Trim());
        return m.Success ? (m.Groups["name"].Value.Trim(), m.Groups["doc"].Value) : (null, null);
    }

    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    [GeneratedRegex(@"^Transfer[eê]ncia (?:enviada|recebida) pelo Pix - (?<name>.+?) - (?<doc>\S+) - ",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex PixRegex();
}
