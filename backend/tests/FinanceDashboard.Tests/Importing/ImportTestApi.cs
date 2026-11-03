using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceDashboard.Tests.Auth;
using FinanceDashboard.Tests.Support;

namespace FinanceDashboard.Tests.Importing;

/// <summary>HTTP helpers shared by the confirm/undo/concurrency/idempotency import tests.</summary>
internal static class ImportTestApi
{
    public const string Password = "Correct-Horse-42!";
    public const string Fit1 = "11111111-1111-4111-8111-111111111111";
    public const string Fit2 = "22222222-2222-4222-8222-222222222222";
    public const string Fit3 = "33333333-3333-4333-8333-333333333333";

    public static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    public static OfxTestFile ThreeLines() => OfxTestFile.Checking()
        .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito -   Padaria   Ficticia", Fit1)
        .Line("20261010000000[-3:BRT]", "250.00", "Transferencia recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", Fit2)
        .Line("20261020000000[-3:BRT]", "-40.50", "Pagamento de fatura", Fit3);

    public static Task<HttpResponseMessage> UploadAsync(AuthedClient c, Guid accountId, byte[] content) =>
        c.PostFileAsync("/api/imports", content, "extrato.ofx",
            new Dictionary<string, string> { ["accountId"] = accountId.ToString() });

    public static async Task<Guid> PreviewAsync(AuthedClient c, Guid accountId, byte[] content)
    {
        var r = await UploadAsync(c, accountId, content);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await JsonOf(r)).GetProperty("batchId").GetGuid();
    }

    public static async Task<Guid> CreateAccountAsync(AuthedClient c, string type = "Checking", string? externalId = null)
    {
        var r = await c.PostAsync("/api/accounts",
            new { name = "Conta " + type, type, institution = "Banco", externalIdentifier = externalId });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> ConfirmAsync(AuthedClient c, Guid batchId, bool acknowledge = false) =>
        c.PostAsync($"/api/imports/{batchId}/confirm", new { acknowledgeBlockingWarnings = acknowledge });

    public static Task<HttpResponseMessage> UndoAsync(AuthedClient c, Guid batchId) =>
        c.PostAsync($"/api/imports/{batchId}/undo");

    public static async Task<JsonElement> ConfirmOkAsync(AuthedClient c, Guid batchId, bool acknowledge = false)
    {
        var r = await ConfirmAsync(c, batchId, acknowledge);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    public static async Task<string?> ErrorOf(HttpResponseMessage r) =>
        (await JsonOf(r)).GetProperty("error").GetString();

    /// <summary>
    /// Fires the same mutation <paramref name="count"/> times at once. One antiforgery token is fetched up
    /// front so the requests themselves are what races.
    /// </summary>
    public static async Task<HttpResponseMessage[]> RaceAsync(AuthedClient c, IReadOnlyList<(string Url, object Body)> calls)
    {
        var token = await AuthTestHelpers.GetCsrfTokenAsync(c.Raw);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = calls.Select(async call =>
        {
            await start.Task;
            using var request = new HttpRequestMessage(HttpMethod.Post, call.Url) { Content = JsonContent.Create(call.Body) };
            request.Headers.Add(AuthTestHelpers.AntiforgeryHeader, token);
            return await c.Raw.SendAsync(request);
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
