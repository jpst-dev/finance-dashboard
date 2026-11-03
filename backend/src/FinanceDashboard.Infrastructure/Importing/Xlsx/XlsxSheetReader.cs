using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace FinanceDashboard.Infrastructure.Importing.Xlsx;

/// <summary><c>Other</c> = boolean, error or any other typed value we never interpret.</summary>
public enum XlsxCellKind { Empty, Number, Text, Other }

/// <param name="Column">0-based column index.</param>
/// <param name="HasFormula">The cell carries a formula; its value is the cached one (never evaluated).</param>
public sealed record XlsxCell(int Column, XlsxCellKind Kind, string? Text, decimal? Number, bool HasFormula);

/// <param name="Number">1-based sheet row number, as in the file.</param>
/// <param name="Cells">Cells present in the file, in ascending column order.</param>
public sealed record XlsxRow(int Number, IReadOnlyList<XlsxCell> Cells)
{
    public XlsxCell? At(int column)
    {
        int lo = 0, hi = Cells.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var c = Cells[mid].Column;
            if (c == column) return Cells[mid];
            if (c < column) lo = mid + 1;
            else hi = mid - 1;
        }
        return null;
    }
}

/// <param name="Rows">Rows present in the file, in ascending row number order.</param>
public sealed record XlsxSheet(bool Date1904, IReadOnlyList<XlsxRow> Rows);

/// <summary>
/// Generic, streaming (SAX, <see cref="OpenXmlReader"/>) reader of the first visible worksheet of an xlsx. The
/// bytes are validated with <see cref="XlsxSafety.Validate"/> first. Cached values only (formulas are never
/// evaluated, only flagged). Errors carry only the kind and the sheet row number, never cell content.
/// </summary>
public static class XlsxSheetReader
{
    public const int MaxRows = 5000;
    public const int MaxColumns = 64;
    public const int MaxCellChars = 2000;
    public const int MaxSharedStrings = 50_000;
    public const long MaxSharedStringChars = 5_000_000;

    /// <summary>The part <see cref="XlsxSafety"/> validates as the workbook control part.</summary>
    private const string WorkbookPartUri = "/xl/workbook.xml";

    private const int MaxSheetRow = 1_048_576;
    private const string NotXlsx = "not_xlsx";
    private const string Limits = "xlsx_limits_exceeded";
    private const string UnsupportedLayout = "unsupported_layout";
    private const string InvalidCell = "invalid_cell";
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static XlsxSheet ReadFirstVisibleSheet(byte[] content)
    {
        XlsxSafety.Validate(content);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = SpreadsheetDocument.Open(stream, false,
                new OpenSettings { MaxCharactersInPart = XlsxSafety.MaxTotalUncompressed });

            var workbook = document.WorkbookPart ?? throw Fail(NotXlsx);
            // Only the part XlsxSafety fully parsed as the workbook may be opened as the workbook.
            if (!string.Equals(workbook.Uri.OriginalString, WorkbookPartUri, StringComparison.OrdinalIgnoreCase))
                throw Fail(NotXlsx);

            var (date1904, sheetRelId) = ReadWorkbook(workbook);
            if (sheetRelId is null) throw Fail(UnsupportedLayout);
            if (!workbook.TryGetPartById(sheetRelId, out var part)) throw Fail(NotXlsx);
            if (part is not WorksheetPart worksheet) throw Fail(UnsupportedLayout);

            var shared = workbook.SharedStringTablePart is { } sst ? ReadSharedStrings(sst) : [];
            return new XlsxSheet(date1904, ReadRows(worksheet, shared));
        }
        catch (Exception e) when (e is OpenXmlPackageException or XmlException or InvalidDataException or IOException
                                      or FormatException or NotSupportedException or ArgumentException)
        {
            throw Fail(NotXlsx);
        }
    }

    private static ImportFormatException Fail(string kind, int? row = null) => new(kind, row);

    // ---- workbook ----

    /// <summary><c>date1904</c> and the relationship id of the first sheet whose state is absent or <c>visible</c>.</summary>
    private static (bool Date1904, string? SheetRelId) ReadWorkbook(WorkbookPart workbook)
    {
        var date1904 = false;
        string? relId = null;
        using var reader = OpenXmlReader.Create(workbook);
        while (reader.Read())
        {
            if (!reader.IsStartElement) continue;
            if (reader.ElementType == typeof(WorkbookProperties))
            {
                var value = Attribute(reader, "date1904", "");
                date1904 = value is not null && (value.Trim() == "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            else if (reader.ElementType == typeof(Sheet) && relId is null)
            {
                var state = Attribute(reader, "state", "");
                if (state is null || state.Trim() == "visible")
                    relId = Attribute(reader, "id", RelationshipsNs) ?? throw Fail(NotXlsx);
            }
        }
        return (date1904, relId);
    }

    private static string? Attribute(OpenXmlReader reader, string localName, string ns)
    {
        foreach (var a in reader.Attributes)
            if (a.LocalName == localName && a.NamespaceUri == ns) return a.Value;
        return null;
    }

    // ---- shared strings ----

    private static List<string> ReadSharedStrings(SharedStringTablePart part)
    {
        var strings = new List<string>();
        long total = 0;
        StringBuilder? item = null;
        var phonetic = 0;
        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            var type = reader.ElementType;
            if (type == typeof(SharedStringItem))
            {
                if (reader.IsStartElement)
                {
                    if (strings.Count >= MaxSharedStrings) throw Fail(Limits);
                    item = new StringBuilder();
                    phonetic = 0;
                }
                else if (item is not null)
                {
                    strings.Add(item.ToString());
                    item = null;
                }
            }
            else if (type == typeof(PhoneticRun))
            {
                phonetic += reader.IsStartElement ? 1 : -1;
            }
            else if (type == typeof(Text) && reader.IsStartElement && item is not null && phonetic == 0)
            {
                var text = reader.GetText();
                item.Append(text);
                total += text.Length;
                if (item.Length > MaxCellChars || total > MaxSharedStringChars) throw Fail(Limits);
            }
        }
        return strings;
    }

    // ---- worksheet ----

    private static List<XlsxRow> ReadRows(WorksheetPart part, List<string> shared)
    {
        var rows = new List<XlsxRow>();
        List<XlsxCell>? cells = null;
        int rowNumber = 0, lastRow = 0, lastColumn = -1;
        CellState? cell = null;

        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            var type = reader.ElementType;
            if (type == typeof(Row))
            {
                if (reader.IsStartElement)
                {
                    if (cells is not null) throw Fail(NotXlsx);
                    if (rows.Count >= MaxRows) throw Fail(Limits);
                    var r = Attribute(reader, "r", "");
                    rowNumber = r is null ? lastRow + 1 : ParseRowNumber(r);
                    if (rowNumber <= lastRow || rowNumber > MaxSheetRow) throw Fail(NotXlsx);
                    cells = new List<XlsxCell>();
                    lastColumn = -1;
                }
                else if (cells is not null)
                {
                    rows.Add(new XlsxRow(rowNumber, cells));
                    lastRow = rowNumber;
                    cells = null;
                }
            }
            else if (type == typeof(Cell))
            {
                if (cells is null) throw Fail(NotXlsx);
                if (reader.IsStartElement)
                {
                    if (cell is not null) throw Fail(NotXlsx);
                    var reference = Attribute(reader, "r", "");
                    var column = reference is null ? lastColumn + 1 : ParseColumn(reference, rowNumber);
                    if (column <= lastColumn) throw Fail(NotXlsx);
                    if (column >= MaxColumns) throw Fail(Limits);
                    lastColumn = column;
                    cell = new CellState(column, Attribute(reader, "t", "")?.Trim());
                }
                else if (cell is not null)
                {
                    cells.Add(Build(cell, shared, rowNumber));
                    cell = null;
                }
            }
            else if (cell is not null)
            {
                ReadCellChild(reader, type, cell);
            }
        }
        if (cells is not null || cell is not null) throw Fail(NotXlsx);
        return rows;
    }

    private sealed class CellState(int column, string? type)
    {
        public int Column { get; } = column;
        public string? Type { get; } = type;
        public bool HasFormula { get; set; }
        public string? Value { get; set; }
        public StringBuilder? Inline { get; set; }
        public int Phonetic { get; set; }
    }

    private static void ReadCellChild(OpenXmlReader reader, Type type, CellState cell)
    {
        if (type == typeof(PhoneticRun))
        {
            cell.Phonetic += reader.IsStartElement ? 1 : -1;
            return;
        }
        if (!reader.IsStartElement) return;
        if (type == typeof(CellFormula))
        {
            cell.HasFormula = true;
        }
        else if (type == typeof(CellValue))
        {
            var value = reader.GetText();
            if (value.Length > MaxCellChars) throw Fail(Limits);
            cell.Value = value;
        }
        else if (type == typeof(InlineString))
        {
            cell.Inline ??= new StringBuilder();
        }
        else if (type == typeof(Text) && cell.Inline is not null && cell.Phonetic == 0)
        {
            cell.Inline.Append(reader.GetText());
            if (cell.Inline.Length > MaxCellChars) throw Fail(Limits);
        }
    }

    private static XlsxCell Build(CellState c, List<string> shared, int row)
    {
        XlsxCell Make(XlsxCellKind kind, string? text = null, decimal? number = null) =>
            new(c.Column, kind, text, number, c.HasFormula);

        switch (c.Type)
        {
            case null or "n":
                if (c.Value is null) return Make(XlsxCellKind.Empty);
                if (!decimal.TryParse(c.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw Fail(InvalidCell, row);
                return Make(XlsxCellKind.Number, number: number);
            case "s":
                if (c.Value is null) return Make(XlsxCellKind.Empty);
                if (!int.TryParse(c.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index >= shared.Count)
                    throw Fail(InvalidCell, row);
                return Make(XlsxCellKind.Text, text: shared[index]);
            case "inlineStr":
                return c.Inline is null ? Make(XlsxCellKind.Empty) : Make(XlsxCellKind.Text, text: c.Inline.ToString());
            case "str":
                return c.Value is null ? Make(XlsxCellKind.Empty) : Make(XlsxCellKind.Text, text: c.Value);
            default: // "b", "e", "d" and anything else: never interpreted
                return Make(XlsxCellKind.Other);
        }
    }

    // ---- references ----

    private static int ParseRowNumber(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw Fail(NotXlsx);

    /// <summary>0-based column of an <c>A1</c> reference; its row part, when present, must be the enclosing row.</summary>
    private static int ParseColumn(string reference, int row)
    {
        var i = 0;
        var column = 0;
        while (i < reference.Length && char.IsAsciiLetter(reference[i]))
        {
            if (i == 3) throw Fail(NotXlsx); // at most "XFD"
            column = column * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
            i++;
        }
        if (i == 0) throw Fail(NotXlsx);
        if (i < reference.Length && ParseRowNumber(reference[i..]) != row) throw Fail(NotXlsx);
        return column - 1;
    }
}
