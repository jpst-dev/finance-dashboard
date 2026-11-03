using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceDashboard.Tests.Auth;
using FinanceDashboard.Tests.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Support;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Privacy;

[Collection(PostgresCollection.Name)]
public sealed class NoSensitiveLoggingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Import_flow_logs_never_contain_descriptions_amounts_names_or_body()
    {
        var logs = new CapturingLoggerProvider();
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync(), new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Trace",
        }) { LoggerProvider = logs };
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        const string memoMarker = "MEMOMARKER-QZ91";
        const string nameMarker = "NOMEMARKER-QZ92";
        const string amountMarker = "7654.32";
        const string notOfxMarker = "CORPOMARKER-QZ93";
        string[] markers = [memoMarker, nameMarker, amountMarker, notOfxMarker, "7654,32"];

        var checking = await CreateAccountAsync(c);
        var card = await CreateAccountAsync(c, "CreditCard");

        var checkingFile = OfxTestFile.Checking()
            .Line("20261005000000[-3:BRT]", "-" + amountMarker, "Compra no debito - " + memoMarker, Guid.NewGuid().ToString())
            .Line("20261010000000[-3:BRT]", "250.00",
                "Transferencia recebida pelo Pix - " + nameMarker + " - ***.123.456-** - BANCO X", Guid.NewGuid().ToString())
            .Build();
        var cardFile = OfxTestFile.Card()
            .Line("20261006000000[-3:BRT]", "-" + amountMarker, memoMarker + " parcela", Guid.NewGuid().ToString())
            .Build();

        foreach (var (acc, bytes) in new[] { (checking, checkingFile), (card, cardFile) })
        {
            var batch = await PreviewAsync(c, acc, bytes);
            await ConfirmOkAsync(c, batch);
            Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batch)).StatusCode);
            var again = await PreviewAsync(c, acc, bytes);
            await ConfirmOkAsync(c, again);
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await UploadAsync(c, checking, Encoding.UTF8.GetBytes("nome;valor\n" + notOfxMarker + ";10\n"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/summary?month=2026-10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/transactions")).StatusCode);

        Assert.NotEmpty(logs.Entries);
        foreach (var marker in markers)
        {
            Assert.DoesNotContain(logs.Entries, e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Itau_import_flow_logs_never_contain_content()
    {
        var logs = new CapturingLoggerProvider();
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync(), new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Trace",
        }) { LoggerProvider = logs };
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);

        const string memoMarker = "MEMOMARKER-QZ81";
        const string acctMarker = "ACCTMARKER-QZ82";
        const string amountMarker = "6543.21";
        const string invoiceMarker = "FATURAMARKER-QZ83";
        const string holderMarker = "TITULARMARKER-QZ84";
        const string cellMarker = "CELULAMARKER-QZ85";
        const string cardNumber = "****9137";
        const string fileMarker = "FILEMARKER-QZ86";
        const string externalId = "00012345";
        string[] markers = [fileMarker, externalId, memoMarker, acctMarker, amountMarker, "6543,21", invoiceMarker, holderMarker, cellMarker, cardNumber];

        // External identifier set and different from the file's ACCTID: the mismatch path runs too.
        var checking = await CreateAccountAsync(c, externalId: externalId);
        var card = await CreateAccountAsync(c, "CreditCard");

        var ofx = OfxTestFile.Itau().AccountId(acctMarker)
            .Line("20261005000000[-3:BRT]", "-" + amountMarker, "PIX QRS " + memoMarker)
            .Line("20261005000000[-3:BRT]", "-" + amountMarker, "PIX QRS " + memoMarker)
            .Line("20261010000000[-3:BRT]", "250.00", "PIX TRANSF " + memoMarker)
            .Build();
        var invoice = XlsxTestFile.Invoice("2026-11-10", total: amountMarker)
            .Line("2026-03-15", invoiceMarker + " PARCELADA", amountMarker, "Parcela 3 de 10", "Virtual", cardNumber,
                name: holderMarker)
            .Line("2026-10-12", "PAGAMENTO EFETUADO", "-" + amountMarker, cardNumber: cardNumber, name: holderMarker)
            .Build();

        foreach (var (acc, bytes, name) in new[] { (checking, ofx, fileMarker + ".ofx"), (card, invoice, fileMarker + ".xlsx") })
        {
            var batch = await PreviewAsync(c, acc, bytes, name);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/imports/{batch}")).StatusCode);
            await ConfirmOkAsync(c, batch, acknowledge: true);
            Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batch)).StatusCode);
            await ConfirmOkAsync(c, await PreviewAsync(c, acc, bytes, name), acknowledge: true);
        }

        // An invoice whose amount cell is text: refused as invalid_cell, the body carries only the code.
        var bad = await UploadAsync(c, card, XlsxTestFile.Invoice("2026-11-10", total: "10.00")
            .Line("2026-10-03", "LOJA " + invoiceMarker, "10.00", cardNumber: cardNumber, name: holderMarker)
            .AmountAsText(0, cellMarker)
            .Build(), fileMarker + ".xlsx");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var badBody = await bad.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(badBody))
        {
            var property = Assert.Single(doc.RootElement.EnumerateObject());
            Assert.Equal("error", property.Name);
            Assert.Equal("invalid_cell", property.Value.GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/summary?month=2026-10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/imports")).StatusCode);

        Assert.NotEmpty(logs.Entries);
        foreach (var marker in markers)
        {
            Assert.DoesNotContain(logs.Entries, e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        // The card suffix alone, as a standalone number (hex/GUID neighbours excluded so random ids cannot match).
        var suffix = new Regex("(?<![0-9A-Za-z-])9137(?![0-9A-Za-z-])");
        Assert.DoesNotContain(logs.Entries, e => suffix.IsMatch(e));
    }
}
