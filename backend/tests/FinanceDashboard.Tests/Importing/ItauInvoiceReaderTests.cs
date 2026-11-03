using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Itaú invoice reader: sign inversion, classification, dates, identity and total check (synthetic files).</summary>
public sealed class ItauInvoiceReaderTests
{
    private static ParsedStatement Read(XlsxTestFile f) => ItauInvoiceReader.Read(f.Build());

    [Fact]
    public void Inverts_sign_both_ways()
    {
        var s = Read(XlsxTestFile.Invoice(total: "90.00")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00")
            .Line("2026-10-10", "PAGAMENTO EFETUADO", "-500.00")
            .Line("2026-10-12", "ESTORNO LOJA FICTICIA", "-30.00"));

        Assert.Equal(ImportSource.ItauCard, s.Source);
        Assert.Equal(-120.00m, s.Lines[0].Amount);
        Assert.Equal(TransactionState.Normal, s.Lines[0].State);
        Assert.Equal(500.00m, s.Lines[1].Amount);
        Assert.Equal(TransactionState.PendingClassification, s.Lines[1].State);
        Assert.Equal(30.00m, s.Lines[2].Amount);
        Assert.Equal(TransactionState.Normal, s.Lines[2].State);
        Assert.Equal("ESTORNO LOJA FICTICIA", s.Lines[2].Memo);
    }

    [Fact]
    public void Pagamento_requires_whole_token_and_positive_amount()
    {
        var s = Read(XlsxTestFile.Invoice(total: "30.00")
            .Line("2026-10-03", "PAGAMENTOS ONLINE FICTICIO", "10.00")
            .Line("2026-10-04", "PAGAMENTO X", "20.00"));
        Assert.All(s.Lines, l => Assert.Equal(TransactionState.Normal, l.State));
    }

    [Fact]
    public void Saldo_anterior_is_pending()
    {
        var s = Read(XlsxTestFile.Invoice(total: "0.00")
            .Line("2026-10-01", "SALDO ANTERIOR", "300.00")
            .Line("2026-10-01", "Saldo rotativo ficticio", "50.00"));
        Assert.All(s.Lines, l => Assert.Equal(TransactionState.PendingClassification, l.State));
    }

    [Theory]
    [InlineData("ENCARGOS FICTICIOS")]
    [InlineData("IOF COMPRA INTERNACIONAL")]
    [InlineData("JUROS DE MORA")]
    [InlineData("ANUIDADE DIFERENCIADA")]
    public void Encargos_iof_juros_anuidade_are_normal(string description)
    {
        var s = Read(XlsxTestFile.Invoice(total: "5.00").Line("2026-10-03", description, "5.00"));
        Assert.Equal(TransactionState.Normal, s.Lines[0].State);
        Assert.Equal(-5.00m, s.Lines[0].Amount);
    }

    [Fact]
    public void Installment_after_first_is_dated_due_minus_one_month()
    {
        var l = Read(XlsxTestFile.Invoice(total: "10.00")
            .Line("2026-03-15", "LOJA FICTICIA", "10.00", "Parcela 3 de 10")).Lines[0];
        Assert.Equal(new DateOnly(2026, 10, 10), l.Date);
        Assert.Equal(new DateOnly(2026, 3, 15), l.PurchaseDate);
        Assert.Equal(new DateOnly(2026, 11, 10), l.StatementDate);
        Assert.Equal(3, l.InstallmentNumber);
        Assert.Equal(10, l.InstallmentTotal);
    }

    [Fact]
    public void First_installment_and_cash_keep_purchase_date()
    {
        var s = Read(XlsxTestFile.Invoice(total: "30.00")
            .Line("2026-10-03", "LOJA FICTICIA", "10.00", "Parcela 1 de 10")
            .Line("2026-10-04", "MERCADO FICTICIO", "20.00"));
        Assert.Equal(new DateOnly(2026, 10, 3), s.Lines[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 3), s.Lines[0].PurchaseDate);
        Assert.Equal(new DateOnly(2026, 10, 4), s.Lines[1].Date);
        Assert.Equal(new DateOnly(2026, 10, 4), s.Lines[1].PurchaseDate);
        Assert.Null(s.Lines[1].InstallmentNumber);
    }

    [Fact]
    public void Due_on_31st_minus_one_month_clamps()
    {
        var l = Read(XlsxTestFile.Invoice(due: "2026-03-31", total: "10.00")
            .Line("2026-01-10", "LOJA FICTICIA", "10.00", "Parcela 2 de 5")).Lines[0];
        Assert.Equal(new DateOnly(2026, 2, 28), l.Date);
    }

    [Fact]
    public void Identical_lines_get_increasing_ordinals()
    {
        var s = Read(XlsxTestFile.Invoice(total: "35.00")
            .Line("2026-10-03", "CAFE FICTICIO", "10.00")
            .Line("2026-10-03", "CAFE FICTICIO", "10.00")
            .Line("2026-10-03", "OUTRO CAFE FICTICIO", "5.00")
            .Line("2026-10-03", "Cafe  Ficticio", "10.00"));
        Assert.Equal([0, 1, 0, 2], s.Lines.Select(l => l.Ordinal));
        Assert.Equal(s.Lines[0].SourceKey, s.Lines[3].SourceKey);
        Assert.NotEqual(s.Lines[0].SourceKey, s.Lines[2].SourceKey);
    }

    [Fact]
    public void Key_contract_matches_spec_components()
    {
        var l = Read(XlsxTestFile.Invoice(due: "2026-11-10", total: "120.00")
            .Line("2026-10-03", "Loja Ficticia", "120.00", "Parcela 2 de 10", "Físico", "****1234")).Lines[0];
        Assert.Equal(SourceKeysTests.InvoiceContractHash, l.SourceKey);
        Assert.Equal(0, l.Ordinal);
        Assert.Equal("1234", l.CardSuffix);
        Assert.Equal(CardKind.Physical, l.CardKind);
    }

    [Fact]
    public void Declared_total_equal_to_non_pending_sum_has_no_warning()
    {
        var s = Read(XlsxTestFile.Invoice(total: "142.50")
            .Line("2026-10-03", "LOJA FICTICIA", "120.00")
            .Line("2026-10-04", "MERCADO FICTICIO", "50.00")
            .Line("2026-10-05", "ESTORNO LOJA FICTICIA", "-30.00")
            .Line("2026-10-06", "IOF COMPRA INTERNACIONAL", "2.50")
            .Line("2026-10-10", "PAGAMENTO EFETUADO", "-500.00"));
        Assert.Empty(s.Warnings);
        Assert.Equal(new ParsedInvoice(new DateOnly(2026, 11, 10), 142.50m), s.Invoice);
    }

    [Fact]
    public void Total_mismatch_is_blocking_overridable_warning()
    {
        var s = Read(XlsxTestFile.Invoice(total: "999.00").Line("2026-10-03", "LOJA FICTICIA", "120.00"));
        var w = Assert.Single(s.Warnings);
        Assert.Equal("InvoiceTotalMismatch", w.Code);
        Assert.Equal(ImportWarning.Blocking, w.Severity);
        Assert.True(w.Overridable);
    }

    [Fact]
    public void Invoice_has_no_period_and_no_checkpoint()
    {
        var s = Read(XlsxTestFile.Invoice(total: "120.00").Line("2026-10-03", "LOJA FICTICIA", "120.00"));
        Assert.Null(s.PeriodStart);
        Assert.Null(s.PeriodEnd);
        Assert.Null(s.LedgerBalance);
        Assert.Null(s.LedgerRead);
        Assert.Null(s.AccountExternalId);
        Assert.NotNull(s.Invoice);
    }

    [Fact]
    public void Reader_exceptions_carry_no_cell_content()
    {
        const string marker = "MARCADORSECRETO";
        XlsxTestFile[] files =
        [
            XlsxTestFile.Invoice().Line("2026-10-03", marker + " LOJA", "10.00").AmountAsText(0, marker + " 10,00"),
            XlsxTestFile.Invoice().Line("2026-10-03", marker + " LOJA", "10.00", installment: marker),
            XlsxTestFile.Invoice().Line("2026-10-03", marker + " LOJA", "10.00", cardNumber: marker, name: marker),
            XlsxTestFile.Invoice().Line("2026-10-03", marker + " LOJA", "10.00", name: marker)
                .Header([.. XlsxTestFile.DefaultHeader, marker]),
            XlsxTestFile.Invoice().Line("2026-10-03", marker, "10.00", name: marker).DateAsText(0, "03/10/2026"),
        ];
        foreach (var f in files)
        {
            var e = Assert.Throws<ImportFormatException>(() => ItauInvoiceReader.Read(f.Build()));
            Assert.DoesNotContain(marker, e.Message);
            Assert.DoesNotContain(marker, e.ToString());
        }
    }
}
