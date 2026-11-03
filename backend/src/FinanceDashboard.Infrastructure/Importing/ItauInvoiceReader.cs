using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing.Xlsx;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>
/// Itaú credit card invoice (xlsx). Signs are inverted to the app's convention (charge positive in the file →
/// negative amount; payment/refund negative in the file → positive). Identity = content key + ordinal. No period,
/// no checkpoint: the batch keeps the due date and the declared total, checked against the non-Pendente lines.
/// </summary>
public static class ItauInvoiceReader
{
    public static ParsedStatement Read(byte[] content)
    {
        // ReadFirstVisibleSheet runs XlsxSafety.Validate before opening the package.
        var invoice = ItauInvoiceLayout.Parse(XlsxSheetReader.ReadFirstVisibleSheet(content));
        var due = invoice.DueDate;

        var keys = invoice.Lines.Select(l => SourceKeys.ContentKey(
            "itau-fatura",
            SourceKeys.FormatDate(due),
            SourceKeys.FormatDate(l.Date),
            SourceKeys.FormatAmount(-l.FileAmount),
            SourceKeys.NormalizeText(l.Description),
            l.InstallmentNumber is { } n ? $"{n}/{l.InstallmentTotal}" : "",
            l.CardSuffix ?? "")).ToList();
        var ordinals = SourceKeys.Ordinals(keys);

        var lines = new List<ParsedLine>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var l = invoice.Lines[i];
            var amount = -l.FileAmount;
            lines.Add(new ParsedLine(keys[i], ordinals[i], ItauRules.InvoiceLineDate(l.Date, due, l.InstallmentNumber),
                amount, l.Description, ItauRules.ClassifyInvoice(l.Description, amount))
            {
                PurchaseDate = l.Date,
                StatementDate = due,
                InstallmentNumber = l.InstallmentNumber,
                InstallmentTotal = l.InstallmentTotal,
                CardSuffix = l.CardSuffix,
                CardKind = l.CardKind,
            });
        }

        var warnings = new List<ImportWarning>();
        if (!ItauRules.InvoiceTotalMatches(invoice.DeclaredTotal, lines.Select(l => (l.Amount, l.State))))
            warnings.Add(ImportWarning.InvoiceTotalMismatch());

        return new ParsedStatement(ImportSource.ItauCard, null, null, null, lines, null)
        {
            Invoice = new ParsedInvoice(due, invoice.DeclaredTotal),
            Warnings = warnings,
        };
    }
}
