using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FinanceDashboard.Tests.Importing;

/// <summary>
/// Synthetic Itaú-invoice-like xlsx builder. Every OOXML part is written by hand (no spreadsheet library),
/// only with fictional data. Layout of the invoice sheet (1-based rows):
/// 1 empty (logo area) · 2 summary labels <c>Cartão | Valor | Vencimento</c> · 3 product, total, due serial ·
/// 4 empty · 5 <c>Lançamentos</c> · 6 column header · 7.. lines · footer row (<c>Total</c> under the
/// description column and the sum under the amount column; with <see cref="FooterFormula"/> it carries
/// <c>SUM(...)</c> plus the cached value).
/// </summary>
public sealed class XlsxTestFile
{
    public static readonly string[] DefaultHeader =
        ["Data", "Lançamento", "Parcelamento", "Valor", "Titularidade", "Nome", "Tipo do cartão", "Número do cartão"];

    public const string ProductName = "ITAU CARD FICTICIO";

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string RelTypeBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string XmlDecl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const int DateStyle = 1;
    private const int MoneyStyle = 2;

    // 1x1 transparent PNG (synthetic).
    private static readonly byte[] LogoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private sealed record InvoiceLine(string Date, string Description, string Amount, string? Installment,
        string? CardKind, string? CardNumber, string Holder, string Name);

    private sealed record Cell(int Col, string? Text = null, string? Number = null, string? Formula = null, int Style = 0);

    private sealed record Row(int Number, List<Cell> Cells);

    private sealed record Rel(string Id, string Type, string Target, bool External);

    private readonly string _due;
    private readonly string _total;
    private readonly List<InvoiceLine> _lines = new();
    private string[] _header = DefaultHeader;
    private bool _logo, _footerFormula, _date1904, _hiddenFirst, _inline, _secondSummary, _noSummary;
    private readonly HashSet<int> _formulaLines = new();
    private readonly Dictionary<int, string> _amountText = new();
    private readonly Dictionary<int, string> _dateText = new();
    private readonly HashSet<int> _blankBefore = new();
    private readonly HashSet<int> _omitBefore = new();
    private DateTimeOffset? _zipTimestamp;
    private readonly List<(string Name, byte[] Data)> _entries = new();
    private readonly List<(string PartName, string Type)> _contentTypes = new();
    private readonly List<(string RelsPart, string Target, bool External, string Type)> _relationships = new();

    private XlsxTestFile(string due, string total)
    {
        _due = due;
        _total = total;
    }

    /// <param name="due">Due date, ISO <c>yyyy-MM-dd</c> (written as an Excel serial).</param>
    /// <param name="total">Declared total, invariant decimal (written verbatim into <c>&lt;v&gt;</c>).</param>
    public static XlsxTestFile Invoice(string due = "2026-11-10", string total = "0.00") => new(due, total);

    /// <param name="date">Line date, ISO <c>yyyy-MM-dd</c> (written as an Excel serial with a dd/mm/yyyy style).</param>
    /// <param name="amount">Amount as in the bank file (purchase positive), invariant decimal.</param>
    public XlsxTestFile Line(string date, string description, string amount, string? installment = null,
        string? cardKind = "Físico", string? cardNumber = "****1234", string holder = "Titular",
        string name = "FULANO FICTICIO")
    {
        _lines.Add(new InvoiceLine(date, description, amount, installment, cardKind, cardNumber, holder, name));
        return this;
    }

    /// <summary>Adds a PNG logo through a drawing anchored over row 1 (media + drawing + relationships).</summary>
    public XlsxTestFile WithLogo() { _logo = true; return this; }

    /// <summary>Footer amount becomes <c>SUM(...)</c> over the amount column, with the cached value.</summary>
    public XlsxTestFile FooterFormula() { _footerFormula = true; return this; }

    /// <summary><c>workbookPr date1904="1"</c>; every date serial is written in the 1904 system.</summary>
    public XlsxTestFile Date1904() { _date1904 = true; return this; }

    /// <summary>Adds a hidden decoy sheet before the invoice sheet (decoy = <c>xl/worksheets/sheet1.xml</c>).</summary>
    public XlsxTestFile HiddenFirstSheet() { _hiddenFirst = true; return this; }

    /// <summary>Text cells become <c>t="inlineStr"</c>; no sharedStrings part is written.</summary>
    public XlsxTestFile InlineStrings() { _inline = true; return this; }

    /// <summary>The amount cell of line <paramref name="index"/> (0-based) carries a formula plus its cached value.</summary>
    public XlsxTestFile FormulaInLine(int index) { _formulaLines.Add(index); return this; }

    /// <summary>The amount cell of line <paramref name="index"/> is written as text.</summary>
    public XlsxTestFile AmountAsText(int index, string text) { _amountText[index] = text; return this; }

    /// <summary>The date cell of line <paramref name="index"/> is written as text.</summary>
    public XlsxTestFile DateAsText(int index, string text) { _dateText[index] = text; return this; }

    /// <summary>
    /// Inserts a blank row before line <paramref name="index"/> (0-based; <c>index == line count</c> = before the
    /// footer). The row is written explicitly with one empty date-styled cell in the date column, as exports do.
    /// </summary>
    public XlsxTestFile BlankRowBefore(int index) { _blankBefore.Add(index); return this; }

    /// <summary>
    /// Skips a row number before line <paramref name="index"/> (0-based; <c>index == line count</c> = before the
    /// footer) without writing any <c>&lt;row&gt;</c> element for it: a gap in the row numbering.
    /// </summary>
    public XlsxTestFile OmitRowBefore(int index) { _omitBefore.Add(index); return this; }

    /// <summary>Last-write time of every generated zip entry (default: the builder's clock), so two builds can differ only in it.</summary>
    public XlsxTestFile ZipTimestamp(DateTimeOffset when) { _zipTimestamp = when; return this; }

    /// <summary>
    /// Replaces the column header: labels in the given order. Line values are placed by label (compared without
    /// accents/case), so this also shuffles or drops columns; unknown labels get empty cells.
    /// </summary>
    public XlsxTestFile Header(params string[] labels) { _header = labels; return this; }

    /// <summary>Appends a second summary block after the footer.</summary>
    public XlsxTestFile SecondSummaryBlock() { _secondSummary = true; return this; }

    /// <summary>Omits the summary block (rows 2-3 stay empty).</summary>
    public XlsxTestFile WithoutSummary() { _noSummary = true; return this; }

    /// <summary>Adds an arbitrary zip entry; an entry with the same name as a generated part replaces it.</summary>
    public XlsxTestFile Entry(string name, byte[] data) { _entries.Add((name, data)); return this; }

    /// <summary>Adds (or replaces) an <c>Override</c> in <c>[Content_Types].xml</c>.</summary>
    public XlsxTestFile ContentType(string partName, string type) { _contentTypes.Add((partName, type)); return this; }

    /// <summary>
    /// Adds a hyperlink relationship to <paramref name="relsPart"/> (e.g. <c>xl/_rels/workbook.xml.rels</c>);
    /// the part is created when the builder does not generate it.
    /// </summary>
    public XlsxTestFile Relationship(string relsPart, string target, bool external) =>
        Relationship(relsPart, target, external, RelTypeBase + "hyperlink");

    /// <summary>Same as the 3-argument overload, with an explicit relationship <c>Type</c> URI.</summary>
    public XlsxTestFile Relationship(string relsPart, string target, bool external, string type)
    {
        _relationships.Add((relsPart, target, external, type));
        return this;
    }

    public byte[] Build() => BuildWith(null);

    // ---- static hostile/raw files ----

    /// <summary>
    /// Valid invoice plus one media entry that inflates to <paramref name="uncompressedBytes"/>. The payload has
    /// a moderate compression ratio (well below <c>MaxRatio</c>), so only the total-size counter can stop it.
    /// </summary>
    public static byte[] ZipBomb(long uncompressedBytes)
    {
        // 1 MiB block: one pseudo-random byte every 32 bytes, zeros elsewhere (deflate ratio roughly 10-20x).
        var block = new byte[1024 * 1024];
        var rng = new Random(42);
        for (var i = 0; i < block.Length; i += 32) block[i] = (byte)rng.Next(1, 256);
        return SampleInvoice().BuildWith(zip =>
        {
            var entry = zip.CreateEntry("xl/media/image2.bin", CompressionLevel.Fastest);
            using var s = entry.Open();
            var left = uncompressedBytes;
            while (left > 0)
            {
                var n = (int)Math.Min(left, block.Length);
                s.Write(block, 0, n);
                left -= n;
            }
        });
    }

    /// <summary>
    /// Valid invoice parts plus one entry whose local and central headers declare 1 KiB uncompressed (with the
    /// CRC of those 1 KiB) while its deflate data inflates to 60 MiB of zeros. Written byte by byte.
    /// </summary>
    public static byte[] LyingSizeBomb()
    {
        const int declared = 1024;
        const long real = 60L * 1024 * 1024;
        var parts = ReadEntries(SampleInvoice().Build());
        var compressed = new MemoryStream();
        using (var d = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var zeros = new byte[1024 * 1024];
            for (long w = 0; w < real; w += zeros.Length) d.Write(zeros);
        }
        var raw = parts.Select(p => RawEntry.Deflated(p.Name, p.Data)).ToList();
        raw.Add(new RawEntry("xl/media/image2.bin", 8, compressed.ToArray(), Crc32.Compute(new byte[declared]), declared));
        return WriteRawZip(raw);
    }

    public enum ZipTamper
    {
        /// <summary>None: the raw writer's output for a valid invoice (control case).</summary>
        None,
        /// <summary>Local header of <c>xl/workbook.xml</c> declares a compressed size different from the central one.</summary>
        LocalCompressedSizeMismatch,
        /// <summary><c>xl/workbook.xml</c> declares compression method 9 (Deflate64) in both headers.</summary>
        Method9,
        /// <summary><c>xl/workbook.xml</c> has the zip encryption flag (bit 0) set in both headers.</summary>
        EncryptionFlag,
        /// <summary><c>xl/workbook.xml</c> declares <c>0xFFFFFFFF</c> uncompressed size (Zip64 marker).</summary>
        Zip64Size,
    }

    /// <summary>Valid invoice written byte by byte by the raw writer, with one header-level tampering.</summary>
    public static byte[] Tampered(ZipTamper tamper)
    {
        var raw = ReadEntries(SampleInvoice().Build()).Select(p => RawEntry.Deflated(p.Name, p.Data)).ToList();
        var i = raw.FindIndex(e => e.Name == "xl/workbook.xml");
        raw[i] = tamper switch
        {
            ZipTamper.None => raw[i],
            ZipTamper.LocalCompressedSizeMismatch => raw[i] with { LocalCompressedSize = (uint)raw[i].Data.Length + 1 },
            ZipTamper.Method9 => raw[i] with { Method = 9 },
            ZipTamper.EncryptionFlag => raw[i] with { Flags = 1 },
            ZipTamper.Zip64Size => raw[i] with { DeclaredSize = uint.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(tamper)),
        };
        return WriteRawZip(raw);
    }

    /// <summary>
    /// Valid invoice written by the raw writer the way Excel streams it: every entry has flag bit 3 (data
    /// descriptor), zero CRC/sizes in the local header and the real ones in a data descriptor after the data
    /// (and in the central directory).
    /// </summary>
    public static byte[] WithDataDescriptors() =>
        WriteRawZip(ReadEntries(SampleInvoice().Build())
            .Select(p => RawEntry.Deflated(p.Name, p.Data) with { Flags = 8 }).ToList());

    /// <summary>OLE/CFB container holding an <c>EncryptedPackage</c> stream name (password-protected xlsx).</summary>
    public static byte[] EncryptedCfb() => FakeCfb("EncryptionInfo", "EncryptedPackage");

    /// <summary>OLE/CFB container with a <c>Workbook</c> stream name (legacy .xls).</summary>
    public static byte[] LegacyXls() => FakeCfb("Workbook", "SummaryInformation");

    /// <summary>Zip with exactly the given entries, in order (duplicates and odd names allowed).</summary>
    public static byte[] Raw(params (string Name, byte[] Data)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(data);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Reads every entry (name, bytes) of a zip produced by this builder.</summary>
    public static List<(string Name, byte[] Data)> ReadEntries(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var list = new List<(string, byte[])>();
        foreach (var e in zip.Entries)
        {
            using var s = e.Open();
            var ms = new MemoryStream();
            s.CopyTo(ms);
            list.Add((e.FullName, ms.ToArray()));
        }
        return list;
    }

    /// <summary>UTF-8 text of one part of a zip produced by this builder.</summary>
    public static string ReadPart(byte[] zipBytes, string name) =>
        Encoding.UTF8.GetString(ReadEntries(zipBytes).Single(e => e.Name == name).Data);

    private static XlsxTestFile SampleInvoice() =>
        Invoice(total: "120.00").Line("2026-10-03", "LOJA FICTICIA", "120.00").WithLogo();

    // ---- building ----

    private byte[] BuildWith(Action<ZipArchive>? extra)
    {
        var parts = GenerateParts();
        foreach (var (name, data) in _entries)
        {
            var i = parts.FindIndex(p => p.Name == name);
            if (i >= 0) parts[i] = (name, data);
            else parts.Add((name, data));
        }

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in parts)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                if (_zipTimestamp is { } when) e.LastWriteTime = when;
                using var s = e.Open();
                s.Write(data);
            }
            extra?.Invoke(zip);
        }
        return ms.ToArray();
    }

    private List<(string Name, byte[] Data)> GenerateParts()
    {
        var invoiceSheet = _hiddenFirst ? "sheet2.xml" : "sheet1.xml";
        var strings = new List<string>();
        var rows = InvoiceRows();
        var invoiceXml = SheetXml(rows, strings, _logo ? "rIdLogo" : null);
        var hiddenXml = _hiddenFirst
            ? SheetXml([new Row(1, [new Cell(0, Text: "PLANILHA OCULTA FICTICIA")])], strings, null)
            : null;

        var rels = new Dictionary<string, List<Rel>>
        {
            ["_rels/.rels"] = [new Rel("rId1", RelTypeBase + "officeDocument", "xl/workbook.xml", false)],
        };
        var wbRels = new List<Rel> { new("rIdInvoice", RelTypeBase + "worksheet", "worksheets/" + invoiceSheet, false) };
        if (_hiddenFirst) wbRels.Add(new Rel("rIdHidden", RelTypeBase + "worksheet", "worksheets/sheet1.xml", false));
        wbRels.Add(new Rel("rIdStyles", RelTypeBase + "styles", "styles.xml", false));
        if (!_inline) wbRels.Add(new Rel("rIdStrings", RelTypeBase + "sharedStrings", "sharedStrings.xml", false));
        rels["xl/_rels/workbook.xml.rels"] = wbRels;
        if (_logo)
        {
            rels["xl/worksheets/_rels/" + invoiceSheet + ".rels"] =
                [new Rel("rIdLogo", RelTypeBase + "drawing", "../drawings/drawing1.xml", false)];
            rels["xl/drawings/_rels/drawing1.xml.rels"] =
                [new Rel("rId1", RelTypeBase + "image", "../media/image1.png", false)];
        }
        var extraId = 0;
        foreach (var (part, target, external, type) in _relationships)
        {
            if (!rels.TryGetValue(part, out var list)) rels[part] = list = new List<Rel>();
            list.Add(new Rel("rIdExtra" + ++extraId, type, target, external));
        }

        var overrides = new List<(string PartName, string Type)>
        {
            ("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
            ("/xl/worksheets/" + invoiceSheet, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
            ("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
        };
        if (_hiddenFirst)
            overrides.Add(("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
        if (!_inline)
            overrides.Add(("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"));
        if (_logo) overrides.Add(("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml"));
        foreach (var (partName, type) in _contentTypes)
        {
            var key = "/" + partName.TrimStart('/');
            overrides.RemoveAll(o => o.PartName == key);
            overrides.Add((key, type));
        }

        var parts = new List<(string Name, byte[] Data)>
        {
            ("[Content_Types].xml", Utf8(ContentTypesXml(overrides))),
            ("_rels/.rels", Utf8(RelsXml(rels["_rels/.rels"]))),
            ("xl/workbook.xml", Utf8(WorkbookXml())),
            ("xl/_rels/workbook.xml.rels", Utf8(RelsXml(rels["xl/_rels/workbook.xml.rels"]))),
            ("xl/styles.xml", Utf8(StylesXml())),
        };
        if (hiddenXml is not null) parts.Add(("xl/worksheets/sheet1.xml", Utf8(hiddenXml)));
        parts.Add(("xl/worksheets/" + invoiceSheet, Utf8(invoiceXml)));
        if (!_inline) parts.Add(("xl/sharedStrings.xml", Utf8(SharedStringsXml(strings))));
        if (_logo)
        {
            parts.Add(("xl/drawings/drawing1.xml", Utf8(DrawingXml())));
            parts.Add(("xl/media/image1.png", LogoPng));
        }
        foreach (var (name, list) in rels)
        {
            if (name is "_rels/.rels" or "xl/_rels/workbook.xml.rels") continue;
            parts.Add((name, Utf8(RelsXml(list))));
        }
        return parts;
    }

    private List<Row> InvoiceRows()
    {
        var cols = _header.Select(Normalize).ToArray();
        int Col(string label) => Array.IndexOf(cols, label);
        var dateCol = Col("DATA");
        var descCol = Col("LANCAMENTO");
        var amountCol = Col("VALOR");

        var rows = new List<Row>();
        var r = 2;
        if (!_noSummary) r = AddSummary(rows, r);
        else r += 2;
        r++; // empty row
        rows.Add(new Row(r++, [new Cell(0, Text: "Lançamentos")]));
        rows.Add(new Row(r++, _header.Select((label, c) => new Cell(c, Text: label)).ToList()));

        var firstLineRow = r;
        decimal sum = 0;
        for (var i = 0; i <= _lines.Count; i++)
        {
            if (_blankBefore.Contains(i))
                rows.Add(new Row(r++, [new Cell(Math.Max(dateCol, 0), Style: DateStyle)]));
            if (_omitBefore.Contains(i)) r++;
            if (i == _lines.Count) break;
            var line = _lines[i];
            sum += decimal.Parse(line.Amount, NumberStyles.Float, CultureInfo.InvariantCulture);
            var cells = new List<Cell>();
            for (var c = 0; c < cols.Length; c++)
            {
                var cell = LineCell(i, line, cols[c], c);
                if (cell is not null) cells.Add(cell);
            }
            rows.Add(new Row(r++, cells));
        }
        var lastLineRow = r - 1;

        var footer = new List<Cell> { new(descCol >= 0 ? descCol : 0, Text: "Total") };
        if (amountCol >= 0)
        {
            var letter = ColumnLetters(amountCol);
            footer.Add(new Cell(amountCol, Number: Fmt(sum), Style: MoneyStyle,
                Formula: _footerFormula ? $"SUM({letter}{firstLineRow}:{letter}{lastLineRow})" : null));
        }
        rows.Add(new Row(r++, footer.OrderBy(c => c.Col).ToList())); // cells must be in column order

        if (_secondSummary) AddSummary(rows, r + 1);
        return rows;
    }

    private Cell? LineCell(int index, InvoiceLine line, string column, int c) => column switch
    {
        "DATA" => _dateText.TryGetValue(index, out var dt)
            ? new Cell(c, Text: dt)
            : new Cell(c, Number: Serial(line.Date).ToString(CultureInfo.InvariantCulture), Style: DateStyle),
        "LANCAMENTO" => TextOrNull(c, line.Description),
        "PARCELAMENTO" => TextOrNull(c, line.Installment),
        "VALOR" => _amountText.TryGetValue(index, out var at)
            ? new Cell(c, Text: at)
            : new Cell(c, Number: line.Amount, Style: MoneyStyle,
                Formula: _formulaLines.Contains(index) ? line.Amount + "*1" : null),
        "TITULARIDADE" => TextOrNull(c, line.Holder),
        "NOME" => TextOrNull(c, line.Name),
        "TIPO DO CARTAO" => TextOrNull(c, line.CardKind),
        "NUMERO DO CARTAO" => TextOrNull(c, line.CardNumber),
        _ => null,
    };

    private static Cell? TextOrNull(int c, string? text) => string.IsNullOrEmpty(text) ? null : new Cell(c, Text: text);

    private int AddSummary(List<Row> rows, int r)
    {
        rows.Add(new Row(r++, [new Cell(0, Text: "Cartão"), new Cell(1, Text: "Valor"), new Cell(2, Text: "Vencimento")]));
        rows.Add(new Row(r++,
        [
            new Cell(0, Text: ProductName),
            new Cell(1, Number: _total, Style: MoneyStyle),
            new Cell(2, Number: Serial(_due).ToString(CultureInfo.InvariantCulture), Style: DateStyle),
        ]));
        return r;
    }

    private int Serial(string isoDate)
    {
        var d = DateOnly.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_date1904) return d.DayNumber - new DateOnly(1904, 1, 1).DayNumber;
        // 1900 system: serial 60 is Excel's phantom 29/02/1900, so dates from 01/03/1900 count from 30/12/1899.
        return d >= new DateOnly(1900, 3, 1)
            ? d.DayNumber - new DateOnly(1899, 12, 30).DayNumber
            : d.DayNumber - new DateOnly(1899, 12, 31).DayNumber;
    }

    private string SheetXml(List<Row> rows, List<string> strings, string? drawingRelId)
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append($"<worksheet xmlns=\"{MainNs}\" xmlns:r=\"{RelNs}\"><sheetData>");
        foreach (var row in rows)
        {
            sb.Append($"<row r=\"{row.Number}\">");
            foreach (var cell in row.Cells)
            {
                var reference = ColumnLetters(cell.Col) + row.Number;
                if (cell.Text is not null)
                {
                    if (_inline)
                    {
                        sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(cell.Text)}</t></is></c>");
                    }
                    else
                    {
                        var idx = strings.IndexOf(cell.Text);
                        if (idx < 0) { idx = strings.Count; strings.Add(cell.Text); }
                        sb.Append($"<c r=\"{reference}\" t=\"s\"><v>{idx}</v></c>");
                    }
                }
                else if (cell.Number is not null)
                {
                    sb.Append($"<c r=\"{reference}\" s=\"{cell.Style}\">");
                    if (cell.Formula is not null) sb.Append($"<f>{Esc(cell.Formula)}</f>");
                    sb.Append($"<v>{Esc(cell.Number)}</v></c>");
                }
                else
                {
                    sb.Append($"<c r=\"{reference}\" s=\"{cell.Style}\"/>");
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (drawingRelId is not null) sb.Append($"<drawing r:id=\"{drawingRelId}\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private string WorkbookXml()
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append($"<workbook xmlns=\"{MainNs}\" xmlns:r=\"{RelNs}\">");
        sb.Append(_date1904 ? "<workbookPr date1904=\"1\"/>" : "<workbookPr/>");
        sb.Append("<sheets>");
        if (_hiddenFirst) sb.Append("<sheet name=\"Oculta\" sheetId=\"2\" state=\"hidden\" r:id=\"rIdHidden\"/>");
        sb.Append("<sheet name=\"Fatura\" sheetId=\"1\" r:id=\"rIdInvoice\"/>");
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string SharedStringsXml(List<string> strings)
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append($"<sst xmlns=\"{MainNs}\" count=\"{strings.Count}\" uniqueCount=\"{strings.Count}\">");
        foreach (var s in strings) sb.Append($"<si><t xml:space=\"preserve\">{Esc(s)}</t></si>");
        sb.Append("</sst>");
        return sb.ToString();
    }

    private static string StylesXml() =>
        XmlDecl +
        $"<styleSheet xmlns=\"{MainNs}\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\"/></numFmts>" +
        "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static string DrawingXml() =>
        XmlDecl +
        "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        $"xmlns:r=\"{RelNs}\">" +
        "<xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
        "<xdr:ext cx=\"952500\" cy=\"285750\"/>" +
        "<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"Logo\"/><xdr:cNvPicPr/></xdr:nvPicPr>" +
        "<xdr:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>" +
        "<xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic>" +
        "<xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>";

    private string ContentTypesXml(List<(string PartName, string Type)> overrides)
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        if (_logo) sb.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
        foreach (var (partName, type) in overrides)
            sb.Append($"<Override PartName=\"{Esc(partName)}\" ContentType=\"{Esc(type)}\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string RelsXml(List<Rel> rels)
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append($"<Relationships xmlns=\"{PkgRelNs}\">");
        foreach (var rel in rels)
        {
            sb.Append($"<Relationship Id=\"{rel.Id}\" Type=\"{rel.Type}\" Target=\"{Esc(rel.Target)}\"");
            if (rel.External) sb.Append(" TargetMode=\"External\"");
            sb.Append("/>");
        }
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    // ---- helpers ----

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string Fmt(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    private static string ColumnLetters(int col)
    {
        var s = "";
        for (var n = col + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static string Normalize(string label)
    {
        var decomposed = label.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return string.Join(' ', sb.ToString().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static byte[] FakeCfb(params string[] streamNames)
    {
        var bytes = new byte[2048];
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        signature.CopyTo(bytes, 0);
        var offset = 1024; // directory-sector-like area holding UTF-16LE stream names
        foreach (var name in streamNames)
        {
            var encoded = Encoding.Unicode.GetBytes(name);
            encoded.CopyTo(bytes, offset);
            offset += 128;
        }
        return bytes;
    }

    private sealed record RawEntry(string Name, ushort Method, byte[] Data, uint Crc, uint DeclaredSize)
    {
        public ushort Flags { get; init; }

        /// <summary>When set, the local header declares this compressed size instead of the real one.</summary>
        public uint? LocalCompressedSize { get; init; }

        public static RawEntry Deflated(string name, byte[] plain)
        {
            var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) d.Write(plain);
            return new RawEntry(name, 8, ms.ToArray(), Crc32.Compute(plain), (uint)plain.Length);
        }
    }

    private static byte[] WriteRawZip(List<RawEntry> entries)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        var offsets = new List<uint>();
        foreach (var e in entries)
        {
            offsets.Add((uint)ms.Position);
            var name = Encoding.ASCII.GetBytes(e.Name);
            w.Write(0x04034b50u);
            w.Write((ushort)20); w.Write(e.Flags); w.Write(e.Method);
            w.Write((ushort)0); w.Write((ushort)0x21);
            var descriptor = (e.Flags & 8) != 0;
            if (descriptor) { w.Write(0u); w.Write(0u); w.Write(0u); }
            else { w.Write(e.Crc); w.Write(e.LocalCompressedSize ?? (uint)e.Data.Length); w.Write(e.DeclaredSize); }
            w.Write((ushort)name.Length); w.Write((ushort)0);
            w.Write(name); w.Write(e.Data);
            if (descriptor)
            {
                w.Write(0x08074b50u);
                w.Write(e.Crc); w.Write((uint)e.Data.Length); w.Write(e.DeclaredSize);
            }
        }
        var cdStart = (uint)ms.Position;
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var name = Encoding.ASCII.GetBytes(e.Name);
            w.Write(0x02014b50u);
            w.Write((ushort)20); w.Write((ushort)20); w.Write(e.Flags); w.Write(e.Method);
            w.Write((ushort)0); w.Write((ushort)0x21);
            w.Write(e.Crc); w.Write((uint)e.Data.Length); w.Write(e.DeclaredSize);
            w.Write((ushort)name.Length); w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0); w.Write(0u);
            w.Write(offsets[i]);
            w.Write(name);
        }
        var cdSize = (uint)ms.Position - cdStart;
        w.Write(0x06054b50u);
        w.Write((ushort)0); w.Write((ushort)0);
        w.Write((ushort)entries.Count); w.Write((ushort)entries.Count);
        w.Write(cdSize); w.Write(cdStart); w.Write((ushort)0);
        w.Flush();
        return ms.ToArray();
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        public static uint Compute(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
