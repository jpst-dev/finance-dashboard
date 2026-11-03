using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

public class NubankRulesTests
{
    [Theory]
    [InlineData("Pagamento de fatura")]
    [InlineData("pagamento de fatura")]
    [InlineData("Pagamento recebido")]
    [InlineData("TRANSFERENCIA ENVIADA pelo Pix - FULANO")]
    [InlineData("Transferência enviada pelo Pix - FULANO")]
    [InlineData("Transferência recebida pelo Pix - FULANA")]
    [InlineData("Pix no Credito - FULANO")]
    [InlineData("Pix no Crédito - FULANO")]
    public void Classifies_prefixes_ignoring_case_and_accents(string memo)
    {
        Assert.Equal(TransactionState.PendingClassification, NubankRules.Classify(ImportSource.NubankChecking, memo));
    }

    [Theory]
    [InlineData("Compra no débito via NuPay - LOJA")]
    [InlineData("Mercado Central")]
    [InlineData("")]
    public void Regular_purchase_is_normal(string memo)
    {
        Assert.Equal(TransactionState.Normal, NubankRules.Classify(ImportSource.NubankCard, memo));
    }

    [Fact]
    public void Extracts_counterparty_and_masked_document_from_pix_memo()
    {
        var (name, doc) = NubankRules.ExtractCounterparty(
            "Transferência recebida pelo Pix - MARIA FICTICIA DA SILVA - •••.123.456-•• - BANCO FICTICIO (0001) Agência: 1 Conta: 123-4");
        Assert.Equal("MARIA FICTICIA DA SILVA", name);
        Assert.Equal("•••.123.456-••", doc);

        var (n2, d2) = NubankRules.ExtractCounterparty("Transferência enviada pelo Pix - JOAO TESTE - 12.345.678/0001-90 - BANCO X");
        Assert.Equal("JOAO TESTE", n2);
        Assert.Equal("12.345.678/0001-90", d2);
    }

    [Theory]
    [InlineData("Pagamento de fatura")]
    [InlineData("Compra no débito via NuPay - LOJA")]
    [InlineData("Pix no Crédito - NOME")]
    public void Non_pix_memo_has_no_counterparty(string memo)
    {
        Assert.Equal((null, null), NubankRules.ExtractCounterparty(memo));
    }
}
