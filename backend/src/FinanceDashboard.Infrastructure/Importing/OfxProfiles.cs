using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing;

public interface IOfxBankProfile
{
    ParsedStatement Build(OfxDocument doc);
}

public static class OfxProfiles
{
    /// <summary>BANKID without leading zeros: 260 (or absent: the Nubank card has none) is Nubank; 341 checking is Itaú.</summary>
    public static IOfxBankProfile For(OfxDocument doc)
    {
        var bankId = doc.BankId?.Trim().TrimStart('0');
        if (doc.BankId is null || bankId == "260") return new NubankOfxProfile();
        if (bankId == "341" && !doc.IsCard) return new ItauOfxProfile();
        throw new ImportFormatException("unsupported_bank", null);
    }
}

public sealed class NubankOfxProfile : IOfxBankProfile
{
    public ParsedStatement Build(OfxDocument doc)
    {
        var source = doc.IsCard ? ImportSource.NubankCard : ImportSource.NubankChecking;
        // Nubank FITIDs are stable ids: ordinal is always 0 (a FITID repeated in the file is the same line).
        var lines = doc.Transactions.Select(t =>
        {
            if (string.IsNullOrEmpty(t.FitId)) throw new ImportFormatException("malformed", t.Line);
            var (name, document) = NubankRules.ExtractCounterparty(t.Memo);
            return new ParsedLine(t.FitId, 0, t.Posted, t.Amount, t.Memo, NubankRules.Classify(source, t.Memo))
            {
                CounterpartyName = name,
                CounterpartyDocument = document,
                PurchaseDate = t.Posted,
            };
        }).ToList();

        return new ParsedStatement(source, doc.AccountId, doc.PeriodStart, doc.PeriodEnd, lines, doc.Ledger)
        {
            LedgerRead = doc.Ledger,
        };
    }
}

public sealed class ItauOfxProfile : IOfxBankProfile
{
    public ParsedStatement Build(OfxDocument doc)
    {
        var keys = doc.Transactions.Select(t => SourceKeys.ContentKey(
            "itau-conta", SourceKeys.FormatDate(t.Posted), SourceKeys.FormatAmount(t.Amount),
            SourceKeys.NormalizeText(t.Memo))).ToList();
        var ordinals = SourceKeys.Ordinals(keys);

        var unknown = 0;
        var lines = new List<ParsedLine>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var t = doc.Transactions[i];
            var (state, known) = ItauRules.ClassifyChecking(t.Memo);
            if (!known) unknown++;
            lines.Add(new ParsedLine(keys[i], ordinals[i], t.Posted, t.Amount, t.Memo, state));
        }

        var warnings = new List<ImportWarning>();
        if (unknown > 0) warnings.Add(ImportWarning.UnknownLineKinds(unknown));

        ParsedBalance? checkpoint = doc.Ledger;
        if (doc.Ledger is { } ledger && lines.Any(l => l.Date > ledger.AsOf))
        {
            checkpoint = null;
            warnings.Add(ImportWarning.LedgerBalanceBeforeLastLine());
        }

        return new ParsedStatement(ImportSource.ItauChecking, doc.AccountId, doc.PeriodStart, doc.PeriodEnd, lines, checkpoint)
        {
            LedgerRead = doc.Ledger,
            Warnings = warnings,
        };
    }
}
