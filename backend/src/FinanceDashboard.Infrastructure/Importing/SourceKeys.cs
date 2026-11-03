using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>
/// Content keys for sources without stable line ids. The normalization and formats here are FROZEN: changing them
/// changes every stored key (re-imports would stop matching), so any change needs a data migration.
/// </summary>
public static partial class SourceKeys
{
    /// <summary>Invariant upper case, accent marks removed (after FormD), runs of whitespace → one space, trimmed.</summary>
    public static string NormalizeText(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToUpperInvariant(c));
        }

        return WhitespaceRun().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Lower-case hex SHA-256 (64 chars) of the parts joined by <c>|</c>, as UTF-8.</summary>
    public static string ContentKey(params string[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));

    /// <summary><c>0.00</c>, invariant; negative zero is <c>0.00</c>.</summary>
    public static string FormatAmount(decimal amount) =>
        (amount == 0m ? 0m : amount).ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>For each key, how many times it already occurred before it in the list (file order): 0, 1, 2…</summary>
    public static int[] Ordinals(IReadOnlyList<string> keys)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new int[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            seen.TryGetValue(keys[i], out var n);
            result[i] = n;
            seen[keys[i]] = n + 1;
        }

        return result;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex WhitespaceRun();
}
