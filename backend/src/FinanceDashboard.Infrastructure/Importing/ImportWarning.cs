namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Preview check result. <see cref="Message"/> never carries file content (no values, names or descriptions).</summary>
public sealed record ImportWarning(string Code, string Severity /* "warning" | "blocking" */, string Message)
{
    public const string Warn = "warning";
    public const string Blocking = "blocking";

    public static ImportWarning AlreadyImported() =>
        new("AlreadyImported", Warn, "This file was already imported; nothing new will be added.");

    public static ImportWarning AccountIdentifierMismatch() =>
        new("AccountIdentifierMismatch", Blocking, "The file's account identifier differs from the selected account's identifier.");

    public static ImportWarning FitidInOtherAccount(int count) =>
        new("FitidInOtherAccount", Blocking, $"{count} line(s) of this file already exist in another account.");

    public static ImportWarning DateOutsidePeriod(int count) =>
        new("DateOutsidePeriod", Warn, $"{count} line(s) are dated outside the file's period.");

    public static ImportWarning BalanceMismatch() =>
        new("BalanceMismatch", Warn, "The file's ledger balance does not match the previous checkpoint plus the lines after it.");

    public static ImportWarning PeriodGap() =>
        new("PeriodGap", Warn, "There is a gap between this file's period and the periods already imported for this account.");

    public static ImportWarning PeriodOverlap() =>
        new("PeriodOverlap", Warn, "This file's period overlaps periods already imported for this account.");
}
