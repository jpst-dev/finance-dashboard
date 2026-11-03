using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

namespace FinanceDashboard.Tests.Importing;

public sealed class PairingRulesTests
{
    [Theory]
    [InlineData(ImportSource.NubankChecking, "Pagamento de fatura", -500.00, PairingRole.InvoiceAccountSide)]
    [InlineData(ImportSource.NubankChecking, "  PAGAMENTO DE FATURA", -500.00, PairingRole.InvoiceAccountSide)]
    [InlineData(ImportSource.NubankCard, "Pagamento recebido", 500.00, PairingRole.InvoiceCardSide)]
    [InlineData(ImportSource.NubankCard, "PAGAMENTO RECEBIDO", 500.00, PairingRole.InvoiceCardSide)]
    [InlineData(ImportSource.NubankChecking, "Transferência enviada pelo Pix - ANA TESTE - •••.111.222-•• - BANCO X", -80.00, PairingRole.Transfer)]
    [InlineData(ImportSource.NubankChecking, "Transferencia recebida pelo Pix - ANA TESTE", 80.00, PairingRole.Transfer)]
    [InlineData(ImportSource.ItauChecking, "FATURA PAGA ITAU UNICLASS", -500.00, PairingRole.InvoiceAccountSide)]
    [InlineData(ImportSource.ItauChecking, "fatura paga itaú", -500.00, PairingRole.InvoiceAccountSide)]
    [InlineData(ImportSource.ItauCard, "PAGAMENTO EFETUADO", 500.00, PairingRole.InvoiceCardSide)]
    [InlineData(ImportSource.ItauCard, "Pagamento efetuado", 500.00, PairingRole.InvoiceCardSide)]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF JOAO FICTICIO", -80.00, PairingRole.Transfer)]
    [InlineData(ImportSource.ItauChecking, "pix transf joão", 80.00, PairingRole.Transfer)]
    public void Maps_description_to_role(ImportSource source, string description, double amount, PairingRole expected) =>
        Assert.Equal(expected, PairingRules.RoleOf(source, description, (decimal)amount));

    [Theory]
    [InlineData(ImportSource.ItauChecking, "PIX CARTAO FICTICIO", -10.00)]
    [InlineData(ImportSource.ItauChecking, "APLICACAO CDB", -10.00)]
    [InlineData(ImportSource.ItauChecking, "RESGATE CDB", 10.00)]
    [InlineData(ImportSource.ItauChecking, "PAG BOLETO LOJA FICTICIA", -10.00)]
    [InlineData(ImportSource.ItauChecking, "CREDITO LIBERAD CONTRATO", 10.00)]
    [InlineData(ImportSource.ItauChecking, "EST CRED ALGO", 10.00)]
    [InlineData(ImportSource.ItauChecking, "PIX QRS LOJA", -10.00)]
    [InlineData(ImportSource.NubankChecking, "Pix no crédito", -10.00)]
    [InlineData(ImportSource.NubankChecking, "Compra no débito - Transferencia enviada", -10.00)]
    [InlineData(ImportSource.NubankChecking, "Pagamento recebido", 10.00)]
    [InlineData(ImportSource.NubankCard, "Pagamento de fatura", 10.00)]
    [InlineData(ImportSource.NubankCard, "Loja Ficticia", -10.00)]
    [InlineData(ImportSource.ItauCard, "PAGAMENTOS ONLINE FICTICIO", 10.00)]
    [InlineData(ImportSource.ItauCard, "SALDO ANTERIOR", 10.00)]
    [InlineData(ImportSource.ItauCard, "LOJA FICTICIA", -10.00)]
    [InlineData(ImportSource.ItauChecking, "PAGAMENTO EFETUADO", -10.00)]
    public void Other_descriptions_are_not_a_role(ImportSource source, string description, double amount) =>
        Assert.Null(PairingRules.RoleOf(source, description, (decimal)amount));

    [Fact]
    public void Itau_card_payment_with_negative_amount_is_not_a_role() =>
        Assert.Null(PairingRules.RoleOf(ImportSource.ItauCard, "PAGAMENTO EFETUADO", -500m));

    [Fact]
    public void Sign_contract_per_reader()
    {
        // Nubank: account side negative, card side positive (OFX amounts kept as-is).
        var nuAcc = StatementReader.Read(OfxTestFile.Checking()
            .Line("20261021000000[-3:BRT]", "-500.00", "Pagamento de fatura", "a1").Build());
        var nuCard = StatementReader.Read(OfxTestFile.Card()
            .Line("20261021000000[-3:BRT]", "500.00", "Pagamento recebido", "c1").Build());
        // Itaú: checking OFX debit negative; invoice file shows the payment as negative, normalized to positive.
        var itAcc = StatementReader.Read(OfxTestFile.Itau()
            .Line("20261021000000", "-500.00", "FATURA PAGA ITAU UNICLASS", "i1").Build());
        var itCard = StatementReader.Read(XlsxTestFile.Invoice(total: "0.00")
            .Line("2026-10-10", "PAGAMENTO EFETUADO", "-500.00").Build());

        Assert.Equal(ImportSource.NubankChecking, nuAcc.Source);
        Assert.Equal(ImportSource.NubankCard, nuCard.Source);
        Assert.Equal(ImportSource.ItauChecking, itAcc.Source);
        Assert.Equal(ImportSource.ItauCard, itCard.Source);

        foreach (var (s, role, negative) in new[]
        {
            (nuAcc, PairingRole.InvoiceAccountSide, true), (nuCard, PairingRole.InvoiceCardSide, false),
            (itAcc, PairingRole.InvoiceAccountSide, true), (itCard, PairingRole.InvoiceCardSide, false),
        })
        {
            var l = Assert.Single(s.Lines);
            Assert.Equal(negative, l.Amount < 0);
            Assert.Equal(role, PairingRules.RoleOf(s.Source, l.Memo, l.Amount));
        }
    }
}
