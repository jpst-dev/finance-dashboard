using System.Net;
using System.Text;
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
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync()) { LoggerProvider = logs };
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
}
