using System.Text;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

public sealed class OfxProfileTests
{
    private static readonly Encoding Cp1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    [Fact]
    public void Bankid_0260_selects_nubank_checking()
    {
        var s = StatementReader.Read(OfxTestFile.Checking().Line("20261015000000", "-1.00", "Mercado", "f1").Build());
        Assert.Equal(ImportSource.NubankChecking, s.Source);
        Assert.Equal("f1", s.Lines[0].SourceKey);
    }

    [Fact]
    public void Card_without_bankid_selects_nubank_card()
    {
        var s = StatementReader.Read(OfxTestFile.Card().Line("20261015000000", "-1.00", "Mercado", "f1").Build());
        Assert.Equal(ImportSource.NubankCard, s.Source);
    }

    [Fact]
    public void Missing_bankid_in_checking_selects_nubank()
    {
        var s = StatementReader.Read(OfxTestFile.Checking().Bank(null).Build());
        Assert.Equal(ImportSource.NubankChecking, s.Source);
    }

    [Fact]
    public void Bankid_0341_selects_itau_checking()
    {
        var s = StatementReader.Read(OfxTestFile.Itau().Line("20261015000000", "-1.00", "Mercado").Build());
        Assert.Equal(ImportSource.ItauChecking, s.Source);
    }

    [Fact]
    public void Other_bankid_is_unsupported_bank()
    {
        var ex = Assert.Throws<ImportFormatException>(() => StatementReader.Read(OfxTestFile.Checking().Bank("0001").Build()));
        Assert.Equal("unsupported_bank", ex.Kind);
    }

    [Fact]
    public void Itau_card_ofx_is_unsupported_bank()
    {
        // A card statement (CCSTMTRS) whose account block carries the Itaú BANKID.
        var text = Cp1252.GetString(OfxTestFile.Card().Build())
            .Replace("<CCACCTFROM>", "<CCACCTFROM><BANKID>0341</BANKID>");
        var ex = Assert.Throws<ImportFormatException>(() => StatementReader.Read(Cp1252.GetBytes(text)));
        Assert.Equal("unsupported_bank", ex.Kind);
    }

    [Fact]
    public void Nubank_line_without_fitid_is_malformed_with_line()
    {
        var text = Cp1252.GetString(OfxTestFile.Checking().Line("20261015000000", "-1.00", "Mercado", "f1").Build())
            .Replace("<FITID>f1</FITID>", "");
        var ex = Assert.Throws<ImportFormatException>(() => StatementReader.Read(Cp1252.GetBytes(text)));
        Assert.Equal("malformed", ex.Kind);
        Assert.NotNull(ex.Line);
        Assert.DoesNotContain("Mercado", ex.Message);
    }
}
