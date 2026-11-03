using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Infrastructure.Importing.Xlsx;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Layout of the Itaú invoice sheet (synthetic files only).</summary>
public sealed class ItauInvoiceLayoutTests
{
    private static RawInvoice Parse(XlsxTestFile f) => ItauInvoiceLayout.Parse(XlsxSheetReader.ReadFirstVisibleSheet(f.Build()));

    private static ImportFormatException Fails(XlsxTestFile f) => Assert.Throws<ImportFormatException>(() => Parse(f));

    private static XlsxTestFile TwoLines() =>
        XlsxTestFile.Invoice(total: "150.00")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00", "Parcela 2 de 10", "Físico", "****1234")
            .Line("2026-10-05", "MERCADO FICTICIO", "30.00", null, "Virtual", "****5678");

    [Fact]
    public void Parses_summary_header_and_lines_by_label()
    {
        var plain = Parse(TwoLines());
        var shuffled = Parse(TwoLines().Header("Número do cartão", "Valor", "Nome", "Lançamento", "Tipo do cartão",
            "Data", "Titularidade", "Parcelamento"));

        Assert.Equal(new DateOnly(2026, 11, 10), plain.DueDate);
        Assert.Equal(150.00m, plain.DeclaredTotal);
        Assert.Equal(2, plain.Lines.Count);
        var first = plain.Lines[0];
        Assert.Equal(new DateOnly(2026, 10, 3), first.Date);
        Assert.Equal("LOJA FICTICIA", first.Description);
        Assert.Equal(2, first.InstallmentNumber);
        Assert.Equal(10, first.InstallmentTotal);
        Assert.Equal(120.00m, first.FileAmount);
        Assert.Equal(CardKind.Physical, first.CardKind);
        Assert.Equal("1234", first.CardSuffix);
        var second = plain.Lines[1];
        Assert.Null(second.InstallmentNumber);
        Assert.Null(second.InstallmentTotal);
        Assert.Equal(CardKind.Virtual, second.CardKind);
        Assert.Equal("5678", second.CardSuffix);

        Assert.Equal(plain.DueDate, shuffled.DueDate);
        Assert.Equal(plain.DeclaredTotal, shuffled.DeclaredTotal);
        Assert.Equal(
            plain.Lines.Select(l => l with { Row = 0 }),
            shuffled.Lines.Select(l => l with { Row = 0 }));
    }

    [Fact]
    public void Footer_formula_is_ignored() =>
        Assert.Equal(2, Parse(TwoLines().FooterFormula()).Lines.Count);

    [Fact]
    public void Formula_in_line_is_invalid_cell()
    {
        var e = Fails(TwoLines().FormulaInLine(1));
        Assert.Equal("invalid_cell", e.Kind);
        Assert.NotNull(e.Line);
    }

    [Fact]
    public void Amount_as_text_is_invalid_cell() =>
        Assert.Equal("invalid_cell", Fails(TwoLines().AmountAsText(0, "R$ 1.234,56")).Kind);

    [Fact]
    public void Date_as_text_inside_table_is_invalid_cell() =>
        Assert.Equal("invalid_cell", Fails(TwoLines().DateAsText(0, "03/10/2026")).Kind);

    [Fact]
    public void Subtotal_row_ends_table()
    {
        var f = TwoLines().Line("2026-10-06", "SUBTOTAL FICTICIO", "150.00").DateAsText(2, "Subtotal");
        var invoice = Parse(f);
        Assert.Equal(2, invoice.Lines.Count);
    }

    [Fact]
    public void Blank_row_followed_by_more_lines_is_unsupported_layout() =>
        Assert.Equal("unsupported_layout", Fails(TwoLines().BlankRowBefore(1)).Kind);

    [Fact]
    public void Blank_row_before_footer_is_fine() =>
        Assert.Equal(2, Parse(TwoLines().BlankRowBefore(2)).Lines.Count);

    [Theory]
    [InlineData("Data")]
    [InlineData("Lançamento")]
    [InlineData("Valor")]
    public void Missing_required_header_is_unsupported_layout(string missing)
    {
        var header = XlsxTestFile.DefaultHeader.Where(h => h != missing).ToArray();
        Assert.Equal("unsupported_layout", Fails(TwoLines().Header(header)).Kind);
    }

    [Fact]
    public void Unknown_header_label_is_unsupported_layout() =>
        Assert.Equal("unsupported_layout",
            Fails(TwoLines().Header([.. XlsxTestFile.DefaultHeader, "Coluna Ficticia"])).Kind);

    [Fact]
    public void Two_summary_blocks_is_unsupported_layout() =>
        Assert.Equal("unsupported_layout", Fails(TwoLines().SecondSummaryBlock()).Kind);

    [Fact]
    public void Missing_summary_is_unsupported_layout() =>
        Assert.Equal("unsupported_layout", Fails(TwoLines().WithoutSummary()).Kind);

    [Fact]
    public void Installment_regex_is_case_insensitive()
    {
        var line = Parse(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.00", "parcela 2 de 10")).Lines[0];
        Assert.Equal(2, line.InstallmentNumber);
        Assert.Equal(10, line.InstallmentTotal);
    }

    [Theory]
    [InlineData("Parcela 11 de 10")]
    [InlineData("2/10")]
    [InlineData("Parcela 0 de 10")]
    public void Invalid_installment_is_invalid_cell(string installment) =>
        Assert.Equal("invalid_cell",
            Fails(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.00", installment)).Kind);

    [Theory]
    [InlineData("Físico", CardKind.Physical)]
    [InlineData("Virtual", CardKind.Virtual)]
    [InlineData("Virtual recorrente", CardKind.VirtualRecurring)]
    [InlineData("Adicional ficticio", CardKind.Other)]
    [InlineData(null, null)]
    public void Card_kind_mapping(string? text, CardKind? expected) =>
        Assert.Equal(expected,
            Parse(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.00", cardKind: text)).Lines[0].CardKind);

    [Fact]
    public void Card_number_masked_suffix()
    {
        Assert.Equal("1234",
            Parse(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.00", cardNumber: "****1234")).Lines[0].CardSuffix);
        Assert.Null(
            Parse(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.00", cardNumber: null)).Lines[0].CardSuffix);
        Assert.Equal("invalid_cell",
            Fails(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.00", cardNumber: "1234")).Kind);
    }

    [Fact]
    public void Empty_description_is_invalid_cell() =>
        Assert.Equal("invalid_cell", Fails(XlsxTestFile.Invoice().Line("2026-10-03", "", "10.00")).Kind);

    [Fact]
    public void Amount_with_more_than_two_decimals_is_invalid_cell() =>
        Assert.Equal("invalid_cell", Fails(XlsxTestFile.Invoice().Line("2026-10-03", "LOJA FICTICIA", "10.001")).Kind);

    [Fact]
    public void Date1904_invoice_reads_same_dates()
    {
        var a = Parse(TwoLines());
        var b = Parse(TwoLines().Date1904());
        Assert.Equal(a.DueDate, b.DueDate);
        Assert.Equal(a.Lines.Select(l => l.Date), b.Lines.Select(l => l.Date));
    }

    [Fact]
    public void Holder_and_name_are_not_kept()
    {
        var invoice = Parse(TwoLines());
        foreach (var line in invoice.Lines)
        foreach (var p in typeof(RawInvoiceLine).GetProperties())
            Assert.DoesNotContain("FULANO FICTICIO", p.GetValue(line)?.ToString() ?? "");
        Assert.DoesNotContain("FULANO FICTICIO", invoice.ToString());
    }
}
