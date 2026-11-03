using System.Text;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Infrastructure.Importing.Xlsx;

namespace FinanceDashboard.Tests.Importing;

public sealed class XlsxSheetReaderTests
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XlsxTestFile Sample() =>
        XlsxTestFile.Invoice(due: "2025-12-09", total: "150.00")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00", "Parcela 2 de 10")
            .Line("2026-10-05", "PADARIA FICTICIA", "30.00", cardKind: "Virtual", cardNumber: "****9876");

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    /// <summary>Valid workbook whose first sheet is replaced by <paramref name="sheetData"/> (and, optionally, the shared strings).</summary>
    private static byte[] WithSheet(string sheetData, string? sharedStrings = null)
    {
        var file = XlsxTestFile.Invoice().Entry("xl/worksheets/sheet1.xml",
            Utf8($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"{MainNs}\"><sheetData>{sheetData}</sheetData></worksheet>"));
        if (sharedStrings is not null)
            file.Entry("xl/sharedStrings.xml",
                Utf8($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"{MainNs}\">{sharedStrings}</sst>"));
        var bytes = file.Build();
        XlsxSafety.Validate(bytes); // the file itself is safe: any rejection below comes from the sheet reader
        return bytes;
    }

    private static ImportFormatException Fails(byte[] content) =>
        Assert.Throws<ImportFormatException>(() => XlsxSheetReader.ReadFirstVisibleSheet(content));

    private static XlsxRow RowNumber(XlsxSheet sheet, int number) => sheet.Rows.Single(r => r.Number == number);

    // ---- values ----

    [Fact]
    public void Reads_shared_and_inline_strings_and_numbers()
    {
        foreach (var inline in new[] { false, true })
        {
            var file = Sample();
            if (inline) file.InlineStrings();
            var sheet = XlsxSheetReader.ReadFirstVisibleSheet(file.Build());

            Assert.False(sheet.Date1904);
            var summary = RowNumber(sheet, 3);
            Assert.Equal(new XlsxCell(0, XlsxCellKind.Text, XlsxTestFile.ProductName, null, false), summary.At(0));
            Assert.Equal(new XlsxCell(1, XlsxCellKind.Number, null, 150.00m, false), summary.At(1));
            Assert.Equal(ExcelDates.FromSerial(summary.At(2)!.Number!.Value, false, 3), new DateOnly(2025, 12, 9));

            var header = RowNumber(sheet, 6);
            Assert.Equal(XlsxTestFile.DefaultHeader, header.Cells.Select(c => c.Text));
            Assert.Equal(Enumerable.Range(0, 8), header.Cells.Select(c => c.Column));

            var line = RowNumber(sheet, 7);
            Assert.Equal("LOJA FICTICIA", line.At(1)!.Text);
            Assert.Equal("Parcela 2 de 10", line.At(2)!.Text);
            Assert.Equal(120.00m, line.At(3)!.Number);
            Assert.Equal("****1234", line.At(7)!.Text);
            Assert.Null(line.At(8));
            Assert.Null(RowNumber(sheet, 8).At(2)); // no installment: no cell
            Assert.DoesNotContain(sheet.Rows, r => r.Number == 1); // rows are the ones present in the file
        }
    }

    [Fact]
    public void Reads_rich_text_str_bool_error_and_empty_cells()
    {
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(WithSheet(
            "<row r=\"2\">" +
            "<c r=\"A2\" t=\"s\"><v>1</v></c>" +
            "<c r=\"B2\" t=\"inlineStr\"><is><r><t>IN</t></r><r><t xml:space=\"preserve\">LINE </t></r><rPh sb=\"0\" eb=\"1\"><t>X</t></rPh></is></c>" +
            "<c r=\"C2\" t=\"str\"><f>\"A\"&amp;\"B\"</f><v>AB</v></c>" +
            "<c r=\"D2\" t=\"b\"><v>1</v></c>" +
            "<c r=\"E2\" t=\"e\"><v>#DIV/0!</v></c>" +
            "<c r=\"F2\" s=\"1\"/>" +
            "<c r=\"AA2\" t=\"n\"><v>-7</v></c>" +
            "</row><row r=\"4\"/>",
            "<si><t>ZERO</t></si><si><r><t>RI</t></r><r><t>CO</t></r><rPh sb=\"0\" eb=\"1\"><t>FON</t></rPh></si>"));

        Assert.Equal(new[] { 2, 4 }, sheet.Rows.Select(r => r.Number));
        var row = sheet.Rows[0];
        Assert.Equal(new XlsxCell(0, XlsxCellKind.Text, "RICO", null, false), row.At(0));
        Assert.Equal(new XlsxCell(1, XlsxCellKind.Text, "INLINE ", null, false), row.At(1));
        Assert.Equal(new XlsxCell(2, XlsxCellKind.Text, "AB", null, true), row.At(2));
        Assert.Equal(XlsxCellKind.Other, row.At(3)!.Kind);
        Assert.Equal(XlsxCellKind.Other, row.At(4)!.Kind);
        Assert.Equal(new XlsxCell(5, XlsxCellKind.Empty, null, null, false), row.At(5));
        Assert.Equal(new XlsxCell(26, XlsxCellKind.Number, null, -7m, false), row.At(26));
        Assert.Empty(sheet.Rows[1].Cells);
    }

    [Fact]
    public void Cells_without_reference_follow_the_previous_one()
    {
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(WithSheet(
            "<row><c><v>1</v></c><c><v>2</v></c><c r=\"E1\"><v>5</v></c><c><v>6</v></c></row><row><c><v>9</v></c></row>"));
        Assert.Equal(new[] { 1, 2 }, sheet.Rows.Select(r => r.Number));
        Assert.Equal(new[] { 0, 1, 4, 5 }, sheet.Rows[0].Cells.Select(c => c.Column));
        Assert.Equal(6m, sheet.Rows[0].At(5)!.Number);
    }

    [Fact]
    public void Formula_cell_returns_cached_value_with_flag()
    {
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(Sample().FooterFormula().FormulaInLine(0).Build());
        var line = RowNumber(sheet, 7);
        Assert.Equal(new XlsxCell(3, XlsxCellKind.Number, null, 120.00m, true), line.At(3));
        Assert.False(RowNumber(sheet, 8).At(3)!.HasFormula);
        var footer = RowNumber(sheet, 9);
        Assert.Equal("Total", footer.At(1)!.Text);
        Assert.Equal(new XlsxCell(3, XlsxCellKind.Number, null, 150.00m, true), footer.At(3));
    }

    [Fact]
    public void Formula_without_cached_value_is_empty_with_flag()
    {
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(WithSheet("<row r=\"1\"><c r=\"A1\"><f>1+1</f></c></row>"));
        Assert.Equal(new XlsxCell(0, XlsxCellKind.Empty, null, null, true), sheet.Rows[0].At(0));
    }

    [Fact]
    public void Detects_date1904()
    {
        Assert.True(XlsxSheetReader.ReadFirstVisibleSheet(Sample().Date1904().Build()).Date1904);
        Assert.False(XlsxSheetReader.ReadFirstVisibleSheet(Sample().Build()).Date1904);
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(Sample().Date1904().Build());
        var due = RowNumber(sheet, 3).At(2)!.Number!.Value;
        Assert.Equal(new DateOnly(2025, 12, 9), ExcelDates.FromSerial(due, sheet.Date1904, 3));
    }

    [Fact]
    public void Skips_hidden_first_sheet()
    {
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(Sample().HiddenFirstSheet().Build());
        Assert.DoesNotContain(sheet.Rows.SelectMany(r => r.Cells), c => c.Text == "PLANILHA OCULTA FICTICIA");
        Assert.Equal("LOJA FICTICIA", RowNumber(sheet, 7).At(1)!.Text);
    }

    [Fact]
    public void No_visible_sheet_is_unsupported_layout()
    {
        var workbook = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            $"<workbook xmlns=\"{MainNs}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<workbookPr/><sheets>" +
            "<sheet name=\"A\" sheetId=\"1\" state=\"hidden\" r:id=\"rIdInvoice\"/>" +
            "<sheet name=\"B\" sheetId=\"2\" state=\"veryHidden\" r:id=\"rIdHidden\"/>" +
            "</sheets></workbook>";
        var bytes = Sample().HiddenFirstSheet().Entry("xl/workbook.xml", Utf8(workbook)).Build();
        XlsxSafety.Validate(bytes);
        var e = Fails(bytes);
        Assert.Equal("unsupported_layout", e.Kind);
        Assert.Null(e.Line);
    }

    // ---- limits ----

    [Fact]
    public void Too_many_rows_is_limits_exceeded()
    {
        string Rows(int n) => string.Concat(Enumerable.Range(1, n).Select(i => $"<row r=\"{i}\"><c r=\"A{i}\"><v>{i}</v></c></row>"));
        Assert.Equal(XlsxSheetReader.MaxRows, XlsxSheetReader.ReadFirstVisibleSheet(WithSheet(Rows(XlsxSheetReader.MaxRows))).Rows.Count);
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet(Rows(XlsxSheetReader.MaxRows + 1))).Kind);
    }

    [Fact]
    public void Column_beyond_limit_is_limits_exceeded()
    {
        // MaxColumns = 64 → last allowed column is index 63 ("BL").
        Assert.Equal(63, XlsxSheetReader.ReadFirstVisibleSheet(WithSheet("<row r=\"1\"><c r=\"BL1\"><v>1</v></c></row>")).Rows[0].Cells[0].Column);
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet("<row r=\"1\"><c r=\"BM1\"><v>1</v></c></row>")).Kind);
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet("<row r=\"1\"><c r=\"XFD1\"><v>1</v></c></row>")).Kind);
    }

    [Fact]
    public void Oversized_cell_or_shared_strings_is_limits_exceeded()
    {
        var max = new string('a', XlsxSheetReader.MaxCellChars);
        var over = max + "a";

        // At the limit: fine.
        var ok = XlsxSheetReader.ReadFirstVisibleSheet(WithSheet(
            $"<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>{max}</t></is></c><c r=\"B1\" t=\"s\"><v>0</v></c></row>",
            $"<si><t>{max}</t></si>"));
        Assert.Equal(max, ok.Rows[0].At(0)!.Text);
        Assert.Equal(max, ok.Rows[0].At(1)!.Text);

        // Inline string, split across runs.
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet(
            $"<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><r><t>{max}</t></r><r><t>a</t></r></is></c></row>")).Kind);
        // Formula string result and raw value.
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet($"<row r=\"1\"><c r=\"A1\" t=\"str\"><v>{over}</v></c></row>")).Kind);
        // One shared string item.
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet("<row r=\"1\"/>", $"<si><r><t>{max}</t></r><r><t>a</t></r></si>")).Kind);
        // Too many shared strings (distinct values so the zip ratio stays low).
        var many = string.Concat(Enumerable.Range(0, XlsxSheetReader.MaxSharedStrings + 1).Select(i => $"<si><t>{i}</t></si>"));
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet("<row r=\"1\"/>", many)).Kind);
        // Too many shared string characters (pseudo-random letters, each item under the cell limit).
        var rng = new Random(7);
        var itemChars = XlsxSheetReader.MaxCellChars - 1;
        var items = (int)(XlsxSheetReader.MaxSharedStringChars / itemChars) + 1;
        var sb = new StringBuilder();
        for (var i = 0; i < items; i++)
        {
            sb.Append("<si><t>");
            for (var k = 0; k < itemChars; k++) sb.Append((char)('a' + rng.Next(26)));
            sb.Append("</t></si>");
        }
        Assert.Equal("xlsx_limits_exceeded", Fails(WithSheet("<row r=\"1\"/>", sb.ToString())).Kind);
    }

    // ---- numbers ----

    [Fact]
    public void Number_parse_is_invariant()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var row = XlsxSheetReader.ReadFirstVisibleSheet(WithSheet(
                "<row r=\"1\"><c r=\"A1\"><v>1234.5</v></c><c r=\"B1\"><v>1E-2</v></c><c r=\"C1\"><v>-0.1</v></c><c r=\"D1\"><v>4.6E4</v></c></row>"))
                .Rows[0];
            Assert.Equal(1234.5m, row.At(0)!.Number);
            Assert.Equal(0.01m, row.At(1)!.Number);
            Assert.Equal(-0.1m, row.At(2)!.Number);
            Assert.Equal(46000m, row.At(3)!.Number);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("R$ 30,00")]
    [InlineData("1,5")]
    [InlineData("NaN")]
    [InlineData("INF")]
    [InlineData("1E+400")]
    [InlineData("SEGREDO_MARCADOR")]
    public void Bad_numeric_cell_is_invalid_cell_with_row_only(string value)
    {
        var e = Fails(WithSheet($"<row r=\"1\"/><row r=\"37\"><c r=\"D37\"><v>{value}</v></c></row>"));
        Assert.Equal("invalid_cell", e.Kind);
        Assert.Equal(37, e.Line);
        Assert.Contains("37", e.Message);
        Assert.DoesNotContain(value, e.Message);
        Assert.DoesNotContain("SEGREDO", e.Message);
    }

    [Fact]
    public void Shared_string_index_out_of_range_is_invalid_cell()
    {
        var e = Fails(WithSheet("<row r=\"12\"><c r=\"A12\" t=\"s\"><v>5</v></c></row>", "<si><t>ZERO</t></si>"));
        Assert.Equal("invalid_cell", e.Kind);
        Assert.Equal(12, e.Line);
        Assert.Equal("invalid_cell", Fails(WithSheet("<row r=\"3\"><c r=\"A3\" t=\"s\"><v>x</v></c></row>", "<si><t>ZERO</t></si>")).Kind);
    }

    [Fact]
    public void Rows_or_cells_out_of_order_are_not_xlsx()
    {
        Assert.Equal("not_xlsx", Fails(WithSheet("<row r=\"5\"/><row r=\"4\"/>")).Kind);
        Assert.Equal("not_xlsx", Fails(WithSheet("<row r=\"1\"><c r=\"B1\"><v>1</v></c><c r=\"A1\"><v>1</v></c></row>")).Kind);
        Assert.Equal("not_xlsx", Fails(WithSheet("<row r=\"1\"><c r=\"A2\"><v>1</v></c></row>")).Kind);
        Assert.Equal("not_xlsx", Fails(WithSheet("<row r=\"0\"/>")).Kind);
    }

    // ---- package / defense in depth ----

    [Fact]
    public void Validates_before_parsing()
    {
        var macro = Sample().Entry("xl/vbaProject.bin", [1, 2, 3]).Build();
        Assert.Equal("xlsx_unsafe_content", Fails(macro).Kind);
        Assert.Equal("encrypted_xlsx", Fails(XlsxTestFile.EncryptedCfb()).Kind);
        Assert.Equal("not_xlsx", Fails(Utf8("not a zip")).Kind);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><worksheet xmlns=\"" + MainNs + "\"><sheetData><row r=\"1\">")]
    [InlineData("<?xml version=\"1.0\"?><worksheet xmlns=\"" + MainNs + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>999</v></c></row></sheetData></worksheet>")]
    [InlineData("<?xml version=\"1.0\"?><worksheet xmlns=\"" + MainNs + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>-1</v></c></row></sheetData></worksheet>")]
    [InlineData("<?xml version=\"1.0\"?><worksheet xmlns=\"" + MainNs + "\"><sheetData><row r=\"99999999999\"/></sheetData></worksheet>")]
    [InlineData("ÿþ garbage")]
    public void Malformed_sheet_part_in_a_valid_zip_is_not_xlsx(string sheet)
    {
        // Whatever the SDK or the XML stack throws on a broken part, the caller only ever sees a kind, never a raw
        // exception (no third-party message, no 500).
        var bytes = Sample().Entry("xl/worksheets/sheet1.xml", Utf8(sheet)).Build();
        var e = Fails(bytes);
        Assert.Contains(e.Kind, new[] { "not_xlsx", "invalid_cell", "xlsx_limits_exceeded" });
        Assert.Null(e.InnerException);
    }

    [Fact]
    public void Main_part_other_than_xl_workbook_xml_is_not_xlsx()
    {
        // _rels/.rels points the officeDocument relationship at another part: XlsxSafety validated xl/workbook.xml
        // as a control part, so the reader must not open a different one as the workbook.
        var original = Sample().Build();
        var workbook = XlsxTestFile.ReadEntries(original).Single(e => e.Name == "xl/workbook.xml").Data;
        var workbookRels = XlsxTestFile.ReadEntries(original).Single(e => e.Name == "xl/_rels/workbook.xml.rels").Data;
        var bytes = Sample()
            .Entry("_rels/.rels", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/book2.xml\"/>" +
                "</Relationships>"))
            .Entry("xl/book2.xml", workbook)
            .Entry("xl/_rels/book2.xml.rels", workbookRels)
            .ContentType("xl/book2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")
            .Build();
        XlsxSafety.Validate(bytes);
        Assert.Equal("not_xlsx", Fails(bytes).Kind);
    }

    [Fact]
    public void Data_descriptor_zip_like_excel_passes_validation_and_reads()
    {
        var bytes = XlsxTestFile.WithDataDescriptors();
        Assert.Equal(0x08, bytes[6] & 0x08); // first local header: flag bit 3
        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 14)); // local CRC zero
        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 18)); // local compressed size zero
        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 22)); // local uncompressed size zero

        XlsxSafety.Validate(bytes);
        var sheet = XlsxSheetReader.ReadFirstVisibleSheet(bytes);
        Assert.Equal("LOJA FICTICIA", RowNumber(sheet, 7).At(1)!.Text);
        Assert.Equal(120.00m, RowNumber(sheet, 7).At(3)!.Number);
    }
}
