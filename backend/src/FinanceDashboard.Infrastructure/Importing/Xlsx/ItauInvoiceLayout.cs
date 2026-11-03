using System.Globalization;
using System.Text.RegularExpressions;
using FinanceDashboard.Domain;

namespace FinanceDashboard.Infrastructure.Importing.Xlsx;

/// <param name="Row">1-based sheet row number.</param>
/// <param name="FileAmount">Amount as in the file (purchase positive), rounded to 2 places.</param>
public sealed record RawInvoiceLine(int Row, DateOnly Date, string Description, int? InstallmentNumber, int? InstallmentTotal,
    decimal FileAmount, CardKind? CardKind, string? CardSuffix);

public sealed record RawInvoice(DateOnly DueDate, decimal DeclaredTotal, IReadOnlyList<RawInvoiceLine> Lines);

/// <summary>
/// Layout of the Itaú invoice sheet, located by labels (compared with <see cref="SourceKeys.NormalizeText"/>), never
/// by fixed positions: summary block (<c>Cartão | Valor | Vencimento</c> + values row), column header and the line
/// table up to the first row whose date cell is not a number. Holder type and name columns are recognized and
/// discarded (never read). Errors carry only the kind and the sheet row number, never cell content.
/// </summary>
public static partial class ItauInvoiceLayout
{
    private const string UnsupportedLayout = "unsupported_layout";
    private const string InvalidCell = "invalid_cell";

    private const string Data = "DATA";
    private const string Lancamento = "LANCAMENTO";
    private const string Parcelamento = "PARCELAMENTO";
    private const string Valor = "VALOR";
    private const string TipoCartao = "TIPO DO CARTAO";
    private const string NumeroCartao = "NUMERO DO CARTAO";

    private static readonly HashSet<string> HeaderLabels =
        [Data, Lancamento, Parcelamento, Valor, "TITULARIDADE", "NOME", TipoCartao, NumeroCartao];

    public static RawInvoice Parse(XlsxSheet sheet)
    {
        var rows = sheet.Rows;
        var (dueDate, total) = ParseSummary(sheet);

        var headerIndex = FindHeader(rows);
        var columns = ParseHeader(rows[headerIndex]);
        var dataCol = columns[Data];
        var valorCol = columns[Valor];

        var lines = new List<RawInvoiceLine>();
        var i = headerIndex + 1;
        var previous = rows[headerIndex].Number;
        for (; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Number != previous + 1) break; // a missing row is a blank row
            var date = row.At(dataCol);
            if (date is null || date.Kind == XlsxCellKind.Empty) break;
            if (date.Kind == XlsxCellKind.Text)
            {
                if (DateLikeText().IsMatch(date.Text!.Trim())) throw Fail(InvalidCell, row);
                break;
            }
            if (date.Kind != XlsxCellKind.Number) throw Fail(InvalidCell, row);

            lines.Add(ParseLine(row, columns, sheet.Date1904));
            previous = row.Number;
        }

        // An invoice without lines is almost surely a layout change: importing nothing would hide it.
        if (lines.Count == 0) throw Fail(UnsupportedLayout, rows[headerIndex]);

        // Nothing that looks like a line (a date, as a number or as date-like text, plus an amount) may come after
        // the end of the table: it would be silently dropped.
        for (; i < rows.Count; i++)
        {
            var row = rows[i];
            var looksLikeDate = row.At(dataCol) switch
            {
                { Kind: XlsxCellKind.Number } => true,
                { Kind: XlsxCellKind.Text, Text: { } text } => DateLikeText().IsMatch(text.Trim()),
                _ => false,
            };
            if (looksLikeDate && row.At(valorCol) is { Kind: not XlsxCellKind.Empty })
                throw Fail(UnsupportedLayout, row);
        }

        return new RawInvoice(dueDate, total, lines);
    }

    // ---- summary ----

    private static (DateOnly DueDate, decimal Total) ParseSummary(XlsxSheet sheet)
    {
        var rows = sheet.Rows;
        int? found = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var labels = TextColumns(rows[i]);
            if (!labels.ContainsKey("CARTAO") || !labels.ContainsKey(Valor) || !labels.ContainsKey("VENCIMENTO")) continue;
            if (found is not null) throw Fail(UnsupportedLayout, rows[i]);
            found = i;
        }
        if (found is not { } index) throw new ImportFormatException(UnsupportedLayout, null);

        var labelRow = rows[index];
        RequireNoFormula(labelRow);
        var columns = TextColumns(labelRow);
        var valuesRow = index + 1 < rows.Count && rows[index + 1].Number == labelRow.Number + 1 ? rows[index + 1] : null;
        if (valuesRow is null) throw new ImportFormatException(InvalidCell, labelRow.Number + 1);
        RequireNoFormula(valuesRow);

        var total = Amount(valuesRow.At(columns[Valor]), valuesRow);
        var due = valuesRow.At(columns["VENCIMENTO"]);
        if (due is not { Kind: XlsxCellKind.Number, Number: { } serial }) throw Fail(InvalidCell, valuesRow);
        return (ExcelDates.FromSerial(serial, sheet.Date1904, valuesRow.Number), total);
    }

    /// <summary>Normalized text of each text cell → its column (first occurrence wins).</summary>
    private static Dictionary<string, int> TextColumns(XlsxRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in row.Cells)
            if (cell.Kind == XlsxCellKind.Text)
                map.TryAdd(SourceKeys.NormalizeText(cell.Text!), cell.Column);
        return map;
    }

    // ---- header ----

    private static int FindHeader(IReadOnlyList<XlsxRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var labels = TextColumns(rows[i]);
            if (labels.ContainsKey(Data) && labels.ContainsKey(Lancamento)) return i;
        }
        throw new ImportFormatException(UnsupportedLayout, null);
    }

    private static Dictionary<string, int> ParseHeader(XlsxRow row)
    {
        RequireNoFormula(row);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in row.Cells)
        {
            if (cell.Kind == XlsxCellKind.Empty) continue;
            if (cell.Kind != XlsxCellKind.Text) throw Fail(UnsupportedLayout, row);
            var label = SourceKeys.NormalizeText(cell.Text!);
            if (label.Length == 0) continue;
            if (!HeaderLabels.Contains(label) || !columns.TryAdd(label, cell.Column)) throw Fail(UnsupportedLayout, row);
        }
        if (!columns.ContainsKey(Data) || !columns.ContainsKey(Lancamento) || !columns.ContainsKey(Valor))
            throw Fail(UnsupportedLayout, row);
        return columns;
    }

    // ---- lines ----

    private static RawInvoiceLine ParseLine(XlsxRow row, Dictionary<string, int> columns, bool date1904)
    {
        RequireNoFormula(row);
        var date = ExcelDates.FromSerial(row.At(columns[Data])!.Number!.Value, date1904, row.Number);

        var descriptionCell = row.At(columns[Lancamento]);
        if (descriptionCell is not { Kind: XlsxCellKind.Text, Text: { } text } || string.IsNullOrWhiteSpace(text))
            throw Fail(InvalidCell, row);

        var amount = Amount(row.At(columns[Valor]), row);
        var (number, total) = Installment(Optional(row, columns, Parcelamento), row);
        var kind = Kind(Optional(row, columns, TipoCartao));
        var suffix = Suffix(Optional(row, columns, NumeroCartao), row);

        return new RawInvoiceLine(row.Number, date, text.Trim(), number, total, amount, kind, suffix);
    }

    /// <summary>The cell of an optional column, or null when the column or the cell is absent/empty.</summary>
    private static XlsxCell? Optional(XlsxRow row, Dictionary<string, int> columns, string label) =>
        columns.TryGetValue(label, out var col) && row.At(col) is { Kind: not XlsxCellKind.Empty } cell ? cell : null;

    /// <summary>A number with at most 2 decimal places (tolerance 0.000001), rounded to 2 places.</summary>
    private static decimal Amount(XlsxCell? cell, XlsxRow row)
    {
        if (cell is not { Kind: XlsxCellKind.Number, Number: { } value }) throw Fail(InvalidCell, row);
        var rounded = Math.Round(value, 2, MidpointRounding.ToEven);
        if (Math.Abs(rounded - value) > 0.000001m) throw Fail(InvalidCell, row);
        return rounded;
    }

    private static (int? Number, int? Total) Installment(XlsxCell? cell, XlsxRow row)
    {
        if (cell is null) return (null, null);
        if (cell.Kind != XlsxCellKind.Text) throw Fail(InvalidCell, row);
        var normalized = SourceKeys.NormalizeText(cell.Text!);
        if (normalized.Length == 0) return (null, null);
        var m = InstallmentText().Match(normalized);
        if (!m.Success) throw Fail(InvalidCell, row);
        var n = int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        var total = int.Parse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        if (n < 1 || n > total) throw Fail(InvalidCell, row);
        return (n, total);
    }

    private static CardKind? Kind(XlsxCell? cell)
    {
        if (cell is null) return null;
        if (cell.Kind != XlsxCellKind.Text) return CardKind.Other;
        return SourceKeys.NormalizeText(cell.Text!) switch
        {
            "" => null,
            "FISICO" => CardKind.Physical,
            "VIRTUAL" => CardKind.Virtual,
            "VIRTUAL RECORRENTE" => CardKind.VirtualRecurring,
            _ => CardKind.Other,
        };
    }

    private static string? Suffix(XlsxCell? cell, XlsxRow row)
    {
        if (cell is null) return null;
        if (cell.Kind != XlsxCellKind.Text) throw Fail(InvalidCell, row);
        var text = cell.Text!.Trim();
        if (text.Length == 0) return null;
        var m = MaskedCardNumber().Match(text);
        return m.Success ? m.Groups[1].Value : throw Fail(InvalidCell, row);
    }

    // ---- helpers ----

    private static void RequireNoFormula(XlsxRow row)
    {
        foreach (var cell in row.Cells)
            if (cell.HasFormula) throw Fail(InvalidCell, row);
    }

    private static ImportFormatException Fail(string kind, XlsxRow row) => new(kind, row.Number);

    // ECMAScript: \d is ASCII [0-9] only (no other Unicode digits reach int.Parse).
    [GeneratedRegex(@"^PARCELA (\d{1,3}) DE (\d{1,3})$", RegexOptions.ECMAScript, matchTimeoutMilliseconds: 500)]
    private static partial Regex InstallmentText();

    [GeneratedRegex(@"^\*+(\d{4})$", RegexOptions.ECMAScript, matchTimeoutMilliseconds: 500)]
    private static partial Regex MaskedCardNumber();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}/\d{2,4}$", RegexOptions.ECMAScript, matchTimeoutMilliseconds: 500)]
    private static partial Regex DateLikeText();
}
