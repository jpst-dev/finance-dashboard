namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Preview check result. <see cref="Message"/> never carries file content (no values, names or descriptions).</summary>
public sealed record ImportWarning(string Code, string Severity /* "warning" | "blocking" */, string Message, bool Overridable = true)
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

    public static ImportWarning InvoiceTotalMismatch() =>
        new("InvoiceTotalMismatch", Blocking, "The sum of the invoice lines does not match the invoice total declared in the file.");

    public static ImportWarning InvoiceHasMissingLines(int count) =>
        new("InvoiceHasMissingLines", Blocking, $"The invoice appears to be missing {count} line(s); import cannot proceed.", Overridable: false);

    public static ImportWarning LedgerBalanceBeforeLastLine() =>
        new("LedgerBalanceBeforeLastLine", Warn, "The file's ledger balance is dated before its last line, so it will not be used as a checkpoint.");

    public static ImportWarning PossibleDuplicate(int count) =>
        new("PossibleDuplicate", Warn, $"{count} line(s) may duplicate lines already imported.");

    public static ImportWarning UnknownLineKinds(int count) =>
        new("UnknownLineKinds", Warn, $"{count} line(s) have an unrecognized kind and were kept as plain transactions.");

    public static ImportWarning UnknownCardSuffix(int count) =>
        new("UnknownCardSuffix", Warn, $"{count} line(s) belong to a card suffix not seen before.");

    public static ImportWarning PeriodOverlap() =>
        new("PeriodOverlap", Warn, "This file's period overlaps periods already imported for this account.");
}
