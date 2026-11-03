using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

/// <summary>Contract of the content keys: changing any of this changes stored keys and needs a migration.</summary>
public sealed class SourceKeysTests
{
    /// <summary>Frozen key of the invoice contract line (shared with the invoice reader's contract test).</summary>
    public const string InvoiceContractHash = "9b7512557b951f38da6f343be4ebed8cde8c1f487551e502bdfa7244da56510d";

    [Fact]
    public void Normalization_is_frozen()
    {
        Assert.Equal("PAO DE ACUCAR", SourceKeys.NormalizeText("  Pão   de\tAçúcar "));
        Assert.Equal("CAO", SourceKeys.NormalizeText("ção"));
        Assert.Equal("", SourceKeys.NormalizeText(""));
    }

    [Fact]
    public void Content_key_contract()
    {
        var checking = SourceKeys.ContentKey("itau-conta", "2026-10-05", "-10.00", "PIX QRS PADARIA FICTICIA");
        Assert.Equal("48d3baf8781065ccc10dae077df3ff95df5cb5eced28ff91a4640210a51e99b0", checking);
        var invoice = SourceKeys.ContentKey("itau-fatura", "2026-11-10", "2026-10-03", "-120.00", "LOJA FICTICIA", "2/10", "1234");
        Assert.Equal(InvoiceContractHash, invoice);
        Assert.Equal(64, checking.Length);
        Assert.True(checking.Length <= 200); // Transactions.SourceKey (MaxSourceKey)
    }

    [Fact]
    public void Amount_and_date_format()
    {
        Assert.Equal("-10.00", SourceKeys.FormatAmount(-10m));
        Assert.Equal("0.00", SourceKeys.FormatAmount(-0.00m));
        Assert.Equal("1234.50", SourceKeys.FormatAmount(1234.5m));
        Assert.Equal("2026-01-05", SourceKeys.FormatDate(new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void Ordinals_count_occurrences_in_file_order() =>
        Assert.Equal([0, 0, 1, 2, 1], SourceKeys.Ordinals(["a", "b", "a", "a", "b"]));
}
