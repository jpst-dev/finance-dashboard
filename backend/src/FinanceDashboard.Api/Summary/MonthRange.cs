using System.Globalization;

namespace FinanceDashboard.Api.Summary;

/// <summary>Parses a strict "yyyy-MM" month into the half-open date range [Start, End).</summary>
public static class MonthRange
{
    public static bool TryParse(string? month, out DateOnly start, out DateOnly end)
    {
        end = default;
        if (month is null
            || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out start))
        {
            start = default;
            return false;
        }

        end = start.AddMonths(1);
        return true;
    }
}
