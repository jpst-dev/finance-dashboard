using System.Text;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

public class NubankOfxReaderTests
{
    private static ParsedStatement Parse(byte[] b) => NubankOfxReader.Parse(b);

    [Fact]
    public void Parses_checking_statement_lines_period_and_ledger_balance()
    {
        var bytes = OfxTestFile.Checking().AccountId("ACC-77")
            .Line("20261031000000[-3:BRT]", "-45.90", "Compra no débito via NuPay - LOJA FICTICIA", "fit-1")
            .Line("20261030000000[-3:BRT]", "1200.00", "Transferência recebida pelo Pix - ANA TESTE - •••.111.222-•• - BANCO X", "fit-2")
            .Ledger("100.50", "20261031000000[-3:BRT]").Build();
        var s = Parse(bytes);
        Assert.Equal(ImportSource.NubankChecking, s.Source);
        Assert.Equal("ACC-77", s.AccountExternalId);
        Assert.Equal(new DateOnly(2026, 10, 1), s.PeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 31), s.PeriodEnd);
        Assert.Equal(2, s.Lines.Count);
        Assert.Equal(new ParsedLine("fit-1", new DateOnly(2026, 10, 31), -45.90m, "Compra no débito via NuPay - LOJA FICTICIA"), s.Lines[0]);
        Assert.Equal(1200.00m, s.Lines[1].Amount);
        Assert.Equal(new ParsedBalance(100.50m, new DateOnly(2026, 10, 31)), s.LedgerBalance);
    }

    [Fact]
    public void Parses_unclosed_leaves_too()
    {
        var s = Parse(OfxTestFile.Checking().Unclosed().Line("20261015000000", "-1.00", "Mercado", "f1").Build());
        Assert.Single(s.Lines);
        Assert.Equal("Mercado", s.Lines[0].Memo);
        Assert.Equal(-1.00m, s.Lines[0].Amount);
    }

    [Fact]
    public void Parses_card_statement_keeping_sign_charge_negative()
    {
        var s = Parse(OfxTestFile.Card().AccountId("CARD-9")
            .Line("20261020000000[-3:BRT]", "-80.00", "Loja Ficticia", "c1")
            .Line("20261021000000[-3:BRT]", "500.00", "Pagamento recebido", "c2").Build());
        Assert.Equal(ImportSource.NubankCard, s.Source);
        Assert.Equal("CARD-9", s.AccountExternalId);
        Assert.Equal(-80.00m, s.Lines[0].Amount);
        Assert.Equal(500.00m, s.Lines[1].Amount);
    }

    [Fact]
    public void Decodes_cp1252_header_accents_intact()
    {
        var bytes = OfxTestFile.Checking().Charset("1252").Line("20261031000000", "-1.00", "Crédito Transferência ção").Build();
        Assert.Contains((byte)0xE9, bytes); // single-byte CP1252 e-acute
        Assert.Equal("Crédito Transferência ção", Parse(bytes).Lines[0].Memo);
    }

    [Fact]
    public void Decodes_utf8_with_bom()
    {
        var bytes = OfxTestFile.Checking().Encoding("UTF-8").Charset("NONE").WithBom()
            .Line("20261031000000", "-1.00", "Transferência enviada").Build();
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal("Transferência enviada", Parse(bytes).Lines[0].Memo);
    }

    [Fact]
    public void Charset_none_is_utf8()
    {
        var bytes = OfxTestFile.Checking().Encoding("UTF-8").Charset("NONE").Line("20261031000000", "-1.00", "Crédito").Build();
        Assert.Equal("Crédito", Parse(bytes).Lines[0].Memo);
    }

    [Theory]
    [InlineData("20261031000000[-3:BRT]", 2026, 10, 31)]
    [InlineData("20261101000000[0:GMT]", 2026, 11, 1)]
    [InlineData("20261231235959[+5:XXX]", 2026, 12, 31)]
    public void Posted_date_is_literal_not_shifted_by_offset(string posted, int y, int m, int d)
    {
        var s = Parse(OfxTestFile.Checking().Line(posted, "-1.00", "x").Build());
        Assert.Equal(new DateOnly(y, m, d), s.Lines[0].Date);
    }

    [Fact]
    public void Missing_ledger_balance_is_null()
    {
        Assert.Null(Parse(OfxTestFile.Checking().NoLedger().Line("20261031000000", "-1.00", "x").Build()).LedgerBalance);
    }

    [Fact]
    public void Empty_period_is_valid()
    {
        var s = Parse(OfxTestFile.Checking().Build());
        Assert.Empty(s.Lines);
    }

    [Fact]
    public void Rejects_empty_file()
    {
        Assert.Equal("empty", Assert.Throws<ImportFormatException>(() => Parse([])).Kind);
        Assert.Equal("empty", Assert.Throws<ImportFormatException>(() => Parse(Encoding.ASCII.GetBytes("  \r\n "))).Kind);
    }

    [Fact]
    public void Rejects_non_ofx_bytes()
    {
        var ex = Assert.Throws<ImportFormatException>(() => Parse([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]));
        Assert.Equal("not_ofx", ex.Kind);
        Assert.Equal("not_ofx", Assert.Throws<ImportFormatException>(() => Parse(Encoding.ASCII.GetBytes("a,b,c\n1,2,3\n"))).Kind);
    }

    [Theory]
    [InlineData("<!DOCTYPE foo [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>")]
    [InlineData("<!ENTITY x \"y\">")]
    [InlineData("<!doctype foo>")]
    public void Rejects_doctype_and_entity(string injected)
    {
        var ex = Assert.Throws<ImportFormatException>(() => Parse(OfxTestFile.Checking().BeforeOfx(injected).Build()));
        Assert.Equal("forbidden_dtd", ex.Kind);
    }

    [Fact]
    public void Rejects_multiple_statements()
    {
        Assert.Equal("unsupported_multiple_statements",
            Assert.Throws<ImportFormatException>(() => Parse(OfxTestFile.Checking().Statements(2).Build())).Kind);
        Assert.Equal("unsupported_multiple_statements",
            Assert.Throws<ImportFormatException>(() => Parse(OfxTestFile.Card().Statements(2).Build())).Kind);
    }

    [Fact]
    public void Rejects_oversized_input()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        Array.Fill(big, (byte)'a');
        Assert.Equal("too_large", Assert.Throws<ImportFormatException>(() => Parse(big)).Kind);
    }

    [Fact]
    public void Rejects_oversized_field()
    {
        var bytes = OfxTestFile.Checking().Line("20261031000000", "-1.00", new string('a', 2500)).Build();
        Assert.Equal("malformed", Assert.Throws<ImportFormatException>(() => Parse(bytes)).Kind);
    }

    [Fact]
    public void Rejects_bad_amount_or_date_as_malformed()
    {
        Assert.Equal("malformed", Assert.Throws<ImportFormatException>(() =>
            Parse(OfxTestFile.Checking().Line("20261031000000", "1.234,56", "x").Build())).Kind);
        Assert.Equal("malformed", Assert.Throws<ImportFormatException>(() =>
            Parse(OfxTestFile.Checking().Line("2026-10-31", "1.00", "x").Build())).Kind);
    }

    [Fact]
    public void Encoding_utf8_wins_over_charset_1252()
    {
        var utf8 = OfxTestFile.Checking().Encoding("UTF-8").Charset("NONE")
            .Line("20261031000000", "-1.00", "Transferência enviada").Build();
        var text = Encoding.UTF8.GetString(utf8).Replace("CHARSET:NONE", "CHARSET:1252");
        Assert.Equal("Transferência enviada", Parse(Encoding.UTF8.GetBytes(text)).Lines[0].Memo);
    }

    [Theory]
    [InlineData("windows-1252")]
    [InlineData("CP1252")]
    [InlineData("ISO-8859-1")]
    [InlineData("latin1")]
    [InlineData("8859-1")]
    public void Accepts_cp1252_and_latin1_aliases(string charset)
    {
        var bytes = OfxTestFile.Checking().Charset("1252").Line("20261031000000", "-1.00", "Crédito").Build();
        var text = Encoding.GetEncoding(1252).GetString(bytes).Replace("CHARSET:1252", "CHARSET:" + charset);
        Assert.Equal("Crédito", Parse(Encoding.GetEncoding(1252).GetBytes(text)).Lines[0].Memo);
    }

    [Theory]
    [InlineData("CHARSET", "KOI8-R")]
    [InlineData("ENCODING", "UTF-16")]
    public void Unknown_charset_or_encoding_is_malformed_with_header_line_and_no_content(string key, string value)
    {
        var text = OfxTestFile.Checking().Build();
        var s = Encoding.GetEncoding(1252).GetString(text);
        s = key == "CHARSET" ? s.Replace("CHARSET:1252", "CHARSET:" + value) : s.Replace("ENCODING:USASCII", "ENCODING:" + value);
        var ex = Assert.Throws<ImportFormatException>(() => Parse(Encoding.ASCII.GetBytes(s)));
        Assert.Equal("malformed", ex.Kind);
        Assert.Equal(key == "ENCODING" ? 5 : 6, ex.Line);
        Assert.DoesNotContain(value, ex.Message);
    }

    [Fact]
    public void Exception_message_has_kind_and_line_but_no_content()
    {
        const string marker = "ZXQ-MARKER-9137";
        var bytes = OfxTestFile.Checking().Line("20261031000000", "not-a-number", "Compra " + marker).Build();
        var ex = Assert.Throws<ImportFormatException>(() => Parse(bytes));
        Assert.DoesNotContain(marker, ex.Message);
        Assert.DoesNotContain("not-a-number", ex.Message);
        Assert.Contains(ex.Kind, ex.Message);
        Assert.NotNull(ex.Line);
        Assert.Contains(ex.Line!.Value.ToString(), ex.Message);
    }
}
