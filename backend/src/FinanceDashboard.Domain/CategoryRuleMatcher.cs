using System.Text.RegularExpressions;

namespace FinanceDashboard.Domain;

public sealed record RuleView(Guid Id, string Pattern, Guid CategoryId, CategoryKind Kind, DateTimeOffset CreatedAt);

public static class CategoryRuleMatcher
{
    public const int MinPatternLength = 3;
    public const int MaxPatternLength = 100;

    private static readonly Regex DateToken = new(@"^\d{1,2}/\d{1,2}(/\d{2}|/\d{4})?$", RegexOptions.Compiled);
    private static readonly Regex NumberToken = new(@"^\d+([.,]\d+)*$", RegexOptions.Compiled);

    public static bool Matches(string normalizedDescription, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(normalizedDescription)) return false;
        var from = 0;
        while (from <= normalizedDescription.Length - pattern.Length)
        {
            var idx = normalizedDescription.IndexOf(pattern, from, StringComparison.Ordinal);
            if (idx < 0) return false;
            var end = idx + pattern.Length;
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(normalizedDescription[idx - 1]);
            var afterOk = end == normalizedDescription.Length || !char.IsLetterOrDigit(normalizedDescription[end]);
            if (beforeOk && afterOk) return true;
            from = idx + 1;
        }
        return false;
    }

    public static Guid? Winner(string originalDescription, decimal amount, IReadOnlyList<RuleView> rules)
    {
        if (amount == 0) return null;
        var normalized = NameKeys.Normalize(originalDescription);
        if (normalized is null) return null;
        var wanted = amount < 0 ? CategoryKind.Expense : CategoryKind.Income;
        return rules
            .Where(r => r.Kind == wanted && Matches(normalized, r.Pattern))
            .OrderByDescending(r => r.Pattern.Length)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefault();
    }

    public static string? NormalizePattern(string? raw)
    {
        var n = NameKeys.Normalize(raw);
        return n is null || n.Length < MinPatternLength || n.Length > MaxPatternLength ? null : n;
    }

    public static string? SuggestPattern(string originalDescription)
    {
        var tokens = (originalDescription ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        return NormalizePattern(string.Join(' ', StripTrailing(tokens)));
    }

    // Masked documents look like "***.123.456-**" (Nubank: "•••.123.456-••"): only mask, digits and separators, with at
    // least one mask char and one digit. Only a TRAILING one is dropped, so a suggestion is always a prefix of the description.
    private static bool IsMaskedDocument(string token) =>
        token.All(c => c is '*' or '•' or '.' or '-' or '/' || char.IsAsciiDigit(c))
        && token.Any(c => c is '*' or '•') && token.Any(char.IsAsciiDigit);

    private static List<string> StripTrailing(List<string> tokens)
    {
        while (tokens.Count > 0)
        {
            var last = tokens[^1];
            if (DateToken.IsMatch(last) || NumberToken.IsMatch(last) || IsMaskedDocument(last)) tokens.RemoveAt(tokens.Count - 1);
            else break;
        }
        return tokens;
    }
}
