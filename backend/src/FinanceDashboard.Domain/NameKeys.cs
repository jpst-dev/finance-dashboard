using System.Globalization;
using System.Text;

namespace FinanceDashboard.Domain;

public static class NameKeys
{
    public const int MinLength = 7;

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(ch) ? ' ' : char.ToUpperInvariant(ch));
        }
        var parts = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    public static bool Coincide(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= MinLength && longer.StartsWith(shorter, StringComparison.Ordinal);
    }
}
