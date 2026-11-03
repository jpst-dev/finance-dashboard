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
    [InlineData(ImportSource.ItauChecking, "CREDITO LIBERAD PIX 1234", 100.00, PairingRole.PixCreditCredit)]
    [InlineData(ImportSource.ItauChecking, "PIX CARTAO Exemplo20 08", -100.00, PairingRole.PixCreditDebit)]
    [InlineData(ImportSource.ItauChecking, "EST CRED LIB PIX 1234", -100.00, PairingRole.PixCreditDebit)]
    public void Maps_description_to_role(ImportSource source, string description, double amount, PairingRole expected) =>
        Assert.Equal(expected, PairingRules.RoleOf(source, description, (decimal)amount));

    [Theory]
    [InlineData(ImportSource.ItauChecking, "PIX CARTAO FICTICIO", 10.00)]
    [InlineData(ImportSource.ItauChecking, "CREDITO LIBERAD PIX 1234", -10.00)]
    [InlineData(ImportSource.ItauChecking, "EST CRED LIB PIX 1234", 10.00)]
    [InlineData(ImportSource.ItauChecking, "APLICACAO CDB", -10.00)]
    [InlineData(ImportSource.ItauChecking, "RESGATE CDB", 10.00)]
    [InlineData(ImportSource.ItauChecking, "PAG BOLETO LOJA FICTICIA", -10.00)]
    [InlineData(ImportSource.ItauChecking, "CREDITO LIBERAD CONTRATO", -10.00)]
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

    [Theory]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF Maria L11 08", "MARIA L")]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF MaRia L19 08", "MARIA L")]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF Maria L 11 08", "MARIA L")]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF JOÃO FICTICIO", "JOAO FICTICIO")]
    [InlineData(ImportSource.NubankChecking, "Transferência enviada pelo Pix - João  da   Silva - •••.111.222-•• - BANCO X", "JOAO DA SILVA")]
    [InlineData(ImportSource.NubankChecking, "Transferencia recebida pelo Pix - ana teste - 12.345.678/0001-90 - BANCO X", "ANA TESTE")]
    public void Counterparty_key_is_normalized(ImportSource source, string description, string expected) =>
        Assert.Equal(expected, PairingRules.CounterpartyKey(source, description));

    [Theory]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF ")]
    [InlineData(ImportSource.ItauChecking, "PIX TRANSF 11 08")]
    [InlineData(ImportSource.ItauChecking, "PAG BOLETO LOJA FICTICIA")]
    [InlineData(ImportSource.NubankChecking, "Pagamento de fatura")]
    [InlineData(ImportSource.NubankChecking, "Transferência enviada pelo Pix - ANA TESTE")]
    [InlineData(ImportSource.NubankCard, "Loja Ficticia")]
    public void Counterparty_key_is_null_when_not_derivable(ImportSource source, string description) =>
        Assert.Null(PairingRules.CounterpartyKey(source, description));

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
