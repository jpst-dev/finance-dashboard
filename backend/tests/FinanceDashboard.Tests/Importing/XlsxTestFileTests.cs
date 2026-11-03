namespace FinanceDashboard.Tests.Importing;

/// <summary>Checks that every builder knob really changes the generated parts (Tasks 5-6 rely on them).</summary>
public sealed class XlsxTestFileTests
{
    private static XlsxTestFile Base() =>
        XlsxTestFile.Invoice("2026-11-10", "150.00")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00", "Parcela 2 de 10")
            .Line("2026-10-05", "PADARIA FICTICIA", "30.00", cardKind: "Virtual", cardNumber: "****9876");

    private static string Sheet(XlsxTestFile f, string name = "xl/worksheets/sheet1.xml") => XlsxTestFile.ReadPart(f.Build(), name);

    private static string Strings(XlsxTestFile f) => XlsxTestFile.ReadPart(f.Build(), "xl/sharedStrings.xml");

    [Fact]
    public void Default_layout_has_literal_labels_and_serials()
    {
        var f = Base();
        var strings = Strings(f);
        foreach (var label in new[] { "Cartão", "Valor", "Vencimento", "Lançamentos", "Data", "Lançamento", "Parcelamento",
                     "Titularidade", "Nome", "Tipo do cartão", "Número do cartão", "Total", "Parcela 2 de 10", "Físico",
                     "****1234", "FULANO FICTICIO", XlsxTestFile.ProductName })
            Assert.Contains($">{label}<", strings);
        var sheet = Sheet(f);
        Assert.Contains("<c r=\"C3\" s=\"1\"><v>46336</v></c>", sheet); // due 2026-11-10 (1900 system)
        Assert.Contains("<c r=\"B3\" s=\"2\"><v>150.00</v></c>", sheet);
        Assert.Contains("<c r=\"A7\" s=\"1\"><v>46298</v></c>", sheet); // 2026-10-03
        Assert.Contains("<c r=\"D7\" s=\"2\"><v>120.00</v></c>", sheet);
        Assert.Contains("<c r=\"D9\" s=\"2\"><v>150.00</v></c>", sheet); // footer sum, no formula
        Assert.DoesNotContain("<f>", sheet);
        Assert.Contains("<workbookPr/>", XlsxTestFile.ReadPart(f.Build(), "xl/workbook.xml"));
    }

    [Fact]
    public void Footer_formula_carries_sum_and_cached_value() =>
        Assert.Contains("<c r=\"D9\" s=\"2\"><f>SUM(D7:D8)</f><v>150.00</v></c>", Sheet(Base().FooterFormula()));

    [Fact]
    public void Date1904_flags_workbook_and_shifts_serials()
    {
        var f = Base().Date1904();
        Assert.Contains("date1904=\"1\"", XlsxTestFile.ReadPart(f.Build(), "xl/workbook.xml"));
        Assert.Contains("<c r=\"A7\" s=\"1\"><v>44836</v></c>", Sheet(f)); // 46298 - 1462
    }

    [Fact]
    public void Hidden_first_sheet_is_a_decoy_before_the_invoice()
    {
        var f = Base().HiddenFirstSheet();
        var wb = XlsxTestFile.ReadPart(f.Build(), "xl/workbook.xml");
        Assert.True(wb.IndexOf("state=\"hidden\"", StringComparison.Ordinal) < wb.IndexOf("name=\"Fatura\"", StringComparison.Ordinal));
        Assert.Contains("PLANILHA OCULTA FICTICIA", Strings(f));
        Assert.Contains("<c r=\"A7\" s=\"1\">", Sheet(f, "xl/worksheets/sheet2.xml"));
    }

    [Fact]
    public void Inline_strings_drop_shared_strings()
    {
        var f = Base().InlineStrings();
        Assert.DoesNotContain(XlsxTestFile.ReadEntries(f.Build()), e => e.Name == "xl/sharedStrings.xml");
        Assert.Contains("t=\"inlineStr\"><is><t xml:space=\"preserve\">LOJA FICTICIA</t></is>", Sheet(f));
    }

    [Fact]
    public void Line_level_overrides_change_only_that_line()
    {
        var sheet = Sheet(Base().FormulaInLine(1).AmountAsText(0, "R$ 1.234,56").DateAsText(1, "05/10/2026").InlineStrings());
        Assert.Contains("<c r=\"D8\" s=\"2\"><f>30.00*1</f><v>30.00</v></c>", sheet);
        Assert.Contains("<c r=\"D7\" t=\"inlineStr\"><is><t xml:space=\"preserve\">R$ 1.234,56</t></is></c>", sheet);
        Assert.Contains("<c r=\"A8\" t=\"inlineStr\"><is><t xml:space=\"preserve\">05/10/2026</t></is></c>", sheet);
    }

    [Fact]
    public void Blank_row_is_explicit_and_shifts_following_rows()
    {
        var sheet = Sheet(Base().BlankRowBefore(1));
        Assert.Contains("<row r=\"8\"><c r=\"A8\" s=\"1\"/></row>", sheet);
        Assert.Contains("<c r=\"A9\" s=\"1\"><v>46300</v></c>", sheet);
    }

    [Fact]
    public void Header_reorders_and_drops_columns_by_label()
    {
        var sheet = Sheet(Base().Header("Valor", "data", "Lançamento", "Coluna Estranha").InlineStrings());
        Assert.Contains("<c r=\"A7\" s=\"2\"><v>120.00</v></c>", sheet);
        Assert.Contains("<c r=\"B7\" s=\"1\"><v>46298</v></c>", sheet);
        Assert.Contains(">Coluna Estranha<", sheet);
        Assert.DoesNotContain("FULANO", sheet);
    }

    [Fact]
    public void Summary_can_be_doubled_or_removed()
    {
        static int Count(string s) => s.Split(">Vencimento<").Length - 1;
        Assert.Equal(2, Count(Sheet(Base().SecondSummaryBlock().InlineStrings())));
        Assert.Equal(0, Count(Sheet(Base().WithoutSummary().InlineStrings())));
    }

    [Fact]
    public void Logo_adds_media_drawing_and_relationships()
    {
        var names = XlsxTestFile.ReadEntries(Base().WithLogo().Build()).Select(e => e.Name).ToList();
        Assert.Contains("xl/media/image1.png", names);
        Assert.Contains("xl/drawings/drawing1.xml", names);
        Assert.Contains("xl/drawings/_rels/drawing1.xml.rels", names);
        Assert.Contains("xl/worksheets/_rels/sheet1.xml.rels", names);
        Assert.Contains("<drawing r:id=\"rIdLogo\"/>", Sheet(Base().WithLogo()));
    }

    [Fact]
    public void Entry_content_type_and_relationship_are_written()
    {
        var bytes = Base()
            .Entry("xl/workbook.xml", "<w/>"u8.ToArray())
            .Entry("xl/extra.bin", [9])
            .ContentType("xl/workbook.xml", "type/one")
            .Relationship("xl/_rels/workbook.xml.rels", "https://example.invalid/", external: true)
            .Relationship("xl/new/_rels/x.xml.rels", "x.xml", external: false)
            .Build();
        var entries = XlsxTestFile.ReadEntries(bytes);
        Assert.Single(entries, e => e.Name == "xl/workbook.xml");
        Assert.Contains(entries, e => e.Name == "xl/extra.bin");
        Assert.Equal("<w/>", XlsxTestFile.ReadPart(bytes, "xl/workbook.xml"));
        var types = XlsxTestFile.ReadPart(bytes, "[Content_Types].xml");
        Assert.Contains("<Override PartName=\"/xl/workbook.xml\" ContentType=\"type/one\"/>", types);
        Assert.Single(types.Split("PartName=\"/xl/workbook.xml\"").Skip(1));
        Assert.Contains("Target=\"https://example.invalid/\" TargetMode=\"External\"", XlsxTestFile.ReadPart(bytes, "xl/_rels/workbook.xml.rels"));
        Assert.DoesNotContain("TargetMode", XlsxTestFile.ReadPart(bytes, "xl/new/_rels/x.xml.rels"));
    }

    [Fact]
    public void Raw_and_cfb_helpers()
    {
        var raw = XlsxTestFile.Raw(("a", [1]), ("a", [2]));
        Assert.Equal(2, XlsxTestFile.ReadEntries(raw).Count);
        Assert.Equal(0xD0, XlsxTestFile.EncryptedCfb()[0]);
        Assert.Equal(0xD0, XlsxTestFile.LegacyXls()[0]);
    }
}
