using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Tests.Auth;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace FinanceDashboard.Tests.Importing;

[Collection(PostgresCollection.Name)]
public sealed class ImportPreviewTests(PostgresFixture postgres)
{
    private const string Password = "Correct-Horse-42!";
    private const string Fit1 = "11111111-1111-4111-8111-111111111111";
    private const string Fit2 = "22222222-2222-4222-8222-222222222222";
    private const string Fit3 = "33333333-3333-4333-8333-333333333333";

    private async Task<ApiFactory> NewFactoryAsync(TimeProvider? time = null) =>
        new(await postgres.CreateDatabaseAsync()) { TimeProvider = time };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> UploadAsync(AuthedClient c, Guid accountId, byte[] content) =>
        c.PostFileAsync("/api/imports", content, "extrato.ofx",
            new Dictionary<string, string> { ["accountId"] = accountId.ToString() });

    private static async Task<JsonElement> PreviewAsync(AuthedClient c, Guid accountId, byte[] content)
    {
        var r = await UploadAsync(c, accountId, content);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonOf(r);
    }

    private static async Task<Guid> CreateAccountAsync(AuthedClient c, string type = "Checking", string? externalId = null)
    {
        var r = await c.PostAsync("/api/accounts",
            new { name = "Conta " + type, type, institution = "Banco", externalIdentifier = externalId });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonOf(r)).GetProperty("id").GetGuid();
    }

    private static OfxTestFile ThreeLines() => OfxTestFile.Checking()
        .Line("20261005000000[-3:BRT]", "-10.00", "Compra no debito - Padaria Ficticia", Fit1)
        .Line("20261010000000[-3:BRT]", "250.00", "Transferencia recebida pelo Pix - FULANO FICTICIO - ***.123.456-** - BANCO X", Fit2)
        .Line("20261020000000[-3:BRT]", "-40.50", "Pagamento de fatura", Fit3);

    private static string[] Codes(JsonElement preview) =>
        preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()!).ToArray();

    private static JsonElement Warning(JsonElement preview, string code) =>
        preview.GetProperty("warnings").EnumerateArray().Single(w => w.GetProperty("code").GetString() == code);

    private static async Task SeedTransactionAsync(ApiFactory f, AuthedClient c, Guid accountId, string fitId,
        DateOnly date, decimal amount, bool deleted = false)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), AccountId = accountId, Date = date, Amount = amount,
            OriginalDescription = "orig", NormalizedDescription = "orig", State = TransactionState.Normal,
            Origin = TransactionOrigin.Imported, SourceKey = fitId, Ordinal = 0, IsDeleted = deleted,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedConfirmedBatchAsync(ApiFactory f, AuthedClient c, Guid accountId, string hash,
        DateOnly? start, DateOnly? end)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        db.ImportBatches.Add(new ImportBatch
        {
            Id = Guid.NewGuid(), AccountId = accountId, Source = ImportSource.NubankChecking, FileHash = hash,
            PeriodStart = start, PeriodEnd = end, State = ImportBatchState.Confirmed, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCheckpointAsync(ApiFactory f, AuthedClient c, Guid accountId, DateOnly date, decimal amount)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        db.Checkpoints.Add(new BalanceCheckpoint
        {
            Id = Guid.NewGuid(), AccountId = accountId, Date = date, Amount = amount,
            Origin = CheckpointOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task Preview_of_checking_file_counts_new_and_writes_no_transactions()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        await using var f = await NewFactoryAsync(time);
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var bytes = ThreeLines().Build();

        var p = await PreviewAsync(c, acc, bytes);

        Assert.Equal("NubankChecking", p.GetProperty("source").GetString());
        Assert.Equal("2026-10-01", p.GetProperty("periodStart").GetString());
        Assert.Equal("2026-10-31", p.GetProperty("periodEnd").GetString());
        Assert.Equal(3, p.GetProperty("newCount").GetInt32());
        Assert.Equal(0, p.GetProperty("existingCount").GetInt32());
        Assert.Empty(Codes(p));
        Assert.Equal("2026-10-31", p.GetProperty("checkpoint").GetProperty("date").GetString());
        Assert.Equal("100.50", p.GetProperty("checkpoint").GetProperty("amount").GetString());
        var lines = p.GetProperty("lines");
        Assert.Equal(3, lines.GetArrayLength());
        Assert.Equal("2026-10-05", lines[0].GetProperty("date").GetString());
        Assert.Equal("-10.00", lines[0].GetProperty("amount").GetString());
        Assert.Equal("Compra no debito - Padaria Ficticia", lines[0].GetProperty("description").GetString());
        Assert.Equal("Normal", lines[0].GetProperty("state").GetString());
        Assert.False(lines[0].GetProperty("existing").GetBoolean());
        Assert.Equal("PendingClassification", lines[1].GetProperty("state").GetString());
        Assert.Equal("PendingClassification", lines[2].GetProperty("state").GetString());

        var batchId = p.GetProperty("batchId").GetGuid();
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(0, await db.Transactions.CountAsync());
            Assert.Equal(0, await db.Checkpoints.CountAsync());
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(batchId, batch.Id);
            Assert.Equal(ImportBatchState.Preview, batch.State);
            Assert.Equal(Sha256(bytes), batch.FileHash);
            Assert.Equal(time.GetUtcNow().AddHours(24), batch.ExpiresAt);
            Assert.Equal(3, batch.NewCount);
            Assert.NotNull(batch.PreviewJson);
            // Confirm (Task 8) works from the stored preview alone: FITIDs and counterparty are kept.
            Assert.Contains(Fit2, batch.PreviewJson);
            Assert.Contains("FULANO FICTICIO", batch.PreviewJson);
        }

        var got = await JsonOf(await c.GetAsync($"/api/imports/{batchId}"));
        Assert.Equal(3, got.GetProperty("lines").GetArrayLength());
        Assert.Equal("Preview", got.GetProperty("state").GetString());
        var list = await JsonOf(await c.GetAsync("/api/imports"));
        Assert.Equal(batchId, Assert.Single(list.EnumerateArray()).GetProperty("batchId").GetGuid());
    }

    [Fact]
    public async Task Preview_of_card_file_into_checking_account_is_422()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var checking = await CreateAccountAsync(c);
        var card = await CreateAccountAsync(c, "CreditCard");
        var cardFile = OfxTestFile.Card().Line("20261005000000[-3:BRT]", "-10.00", "Loja Ficticia").Build();

        var r = await UploadAsync(c, checking, cardFile);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("account_type_mismatch", (await JsonOf(r)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UploadAsync(c, card, ThreeLines().Build())).StatusCode);

        var ok = await PreviewAsync(c, card, cardFile);
        Assert.Equal("NubankCard", ok.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Invalid_file_is_422_with_kind_and_without_content()
    {
        var logs = new CapturingLoggerProvider();
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync()) { LoggerProvider = logs };
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        const string marker = "SEGREDO-MARCADOR-987";

        var notOfx = await UploadAsync(c, acc, Encoding.UTF8.GetBytes("nome;valor\n" + marker + ";10\n"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notOfx.StatusCode);
        var body = await notOfx.Content.ReadAsStringAsync();
        Assert.Equal("not_ofx", JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(marker, body);

        var dtd = await UploadAsync(c, acc,
            ThreeLines().BeforeOfx("<!DOCTYPE x [<!ENTITY e \"" + marker + "\">]>").Build());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dtd.StatusCode);
        var dtdBody = await dtd.Content.ReadAsStringAsync();
        Assert.Equal("forbidden_dtd", JsonDocument.Parse(dtdBody).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(marker, dtdBody);

        var empty = await UploadAsync(c, acc, []);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal("empty", (await JsonOf(empty)).GetProperty("error").GetString());

        Assert.DoesNotContain(logs.Entries, e => e.Contains(marker, StringComparison.Ordinal));
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.ImportBatches.CountAsync());
    }

    [Fact]
    public async Task Oversized_file_is_413()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        // Just over the file limit (fits the request envelope) and well over the request limit.
        var justOver = new byte[5 * 1024 * 1024 + 1];
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(c, acc, justOver)).StatusCode);
        var huge = new byte[7 * 1024 * 1024];
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(c, acc, huge)).StatusCode);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.ImportBatches.CountAsync());
    }

    [Fact]
    public async Task Existing_fitids_are_reported_existing_including_soft_deleted()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await SeedTransactionAsync(f, c, acc, Fit1, new DateOnly(2026, 10, 5), -10m);
        await SeedTransactionAsync(f, c, acc, Fit2, new DateOnly(2026, 10, 10), 250m, deleted: true);

        var p = await PreviewAsync(c, acc, ThreeLines().Build());

        Assert.Equal(1, p.GetProperty("newCount").GetInt32());
        Assert.Equal(2, p.GetProperty("existingCount").GetInt32());
        var existing = p.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("existing").GetBoolean()).ToArray();
        Assert.Equal(new[] { true, true, false }, existing);
        Assert.DoesNotContain("FitidInOtherAccount", Codes(p));
    }

    [Fact]
    public async Task Same_hash_confirmed_reports_already_imported_with_zero_new()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var bytes = ThreeLines().Build();
        await SeedConfirmedBatchAsync(f, c, acc, Sha256(bytes), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        var p = await PreviewAsync(c, acc, bytes);

        Assert.Equal(0, p.GetProperty("newCount").GetInt32());
        var w = Warning(p, "AlreadyImported");
        Assert.Equal("warning", w.GetProperty("severity").GetString());
        Assert.False(string.IsNullOrWhiteSpace(w.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Same_hash_with_open_preview_replaces_it()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var bytes = ThreeLines().Build();
        var other = OfxTestFile.Checking().Line("20261003000000[-3:BRT]", "-1.00", "Outra").Build();

        var first = (await PreviewAsync(c, acc, bytes)).GetProperty("batchId").GetGuid();
        var otherId = (await PreviewAsync(c, acc, other)).GetProperty("batchId").GetGuid();
        var second = (await PreviewAsync(c, acc, bytes)).GetProperty("batchId").GetGuid();

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/imports/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/imports/{second}")).StatusCode);
        var ids = (await JsonOf(await c.GetAsync("/api/imports"))).EnumerateArray()
            .Select(b => b.GetProperty("batchId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { second, otherId }, ids);
    }

    [Fact]
    public async Task Expired_previews_are_discarded_on_next_upload()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        await using var f = await NewFactoryAsync(time);
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        var old = (await PreviewAsync(c, acc, ThreeLines().Build())).GetProperty("batchId").GetGuid();
        time.Advance(TimeSpan.FromHours(23));
        var recent = (await PreviewAsync(c, acc, OfxTestFile.Checking().Line("20261003000000[-3:BRT]", "-1.00", "A").Build()))
            .GetProperty("batchId").GetGuid();
        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            Assert.Equal(2, await db.ImportBatches.CountAsync());
        }

        time.Advance(TimeSpan.FromHours(2)); // old is now 25 h old, recent 2 h
        var fresh = (await PreviewAsync(c, acc, OfxTestFile.Checking().Line("20261004000000[-3:BRT]", "-2.00", "B").Build()))
            .GetProperty("batchId").GetGuid();

        await using (var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId))
        {
            var ids = await db.ImportBatches.Select(b => b.Id).ToListAsync();
            Assert.Equal(new HashSet<Guid> { recent, fresh }, ids.ToHashSet());
        }

        time.Advance(TimeSpan.FromHours(23)); // recent expired; listing also discards
        var listed = (await JsonOf(await c.GetAsync("/api/imports"))).EnumerateArray()
            .Select(b => b.GetProperty("batchId").GetGuid()).ToList();
        Assert.Equal(new[] { fresh }, listed);
        Assert.DoesNotContain(old, listed);
    }

    [Fact]
    public async Task Account_identifier_mismatch_is_blocking_warning()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var matching = await CreateAccountAsync(c, externalId: "ACC-0001");
        var other = await CreateAccountAsync(c, externalId: "ACC-9999");

        Assert.DoesNotContain("AccountIdentifierMismatch", Codes(await PreviewAsync(c, matching, ThreeLines().Build())));

        var p = await PreviewAsync(c, other, ThreeLines().Build());
        var w = Warning(p, "AccountIdentifierMismatch");
        Assert.Equal("blocking", w.GetProperty("severity").GetString());
        Assert.DoesNotContain("ACC-0001", w.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Fitid_in_other_account_is_blocking_warning()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var elsewhere = await CreateAccountAsync(c);
        await SeedTransactionAsync(f, c, elsewhere, Fit3, new DateOnly(2026, 10, 20), -40.5m);

        var p = await PreviewAsync(c, acc, ThreeLines().Build());

        Assert.Equal("blocking", Warning(p, "FitidInOtherAccount").GetProperty("severity").GetString());
        Assert.Equal(3, p.GetProperty("newCount").GetInt32());
    }

    [Fact]
    public async Task Date_outside_period_and_balance_mismatch_warn()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        await SeedCheckpointAsync(f, c, acc, new DateOnly(2026, 9, 30), 100m);
        // Already-stored visible line after the checkpoint counts; a soft-deleted one does not.
        await SeedTransactionAsync(f, c, acc, Fit1, new DateOnly(2026, 10, 5), -10m);
        await SeedTransactionAsync(f, c, acc, "deleted-key", new DateOnly(2026, 10, 6), -999m, deleted: true);

        // 100 - 10 (existing) + 250 + -40.50 (new) = 299.50
        var matching = ThreeLines().Ledger("299.50", "20261031000000[-3:BRT]").Build();
        var ok = await PreviewAsync(c, acc, matching);
        Assert.Empty(Codes(ok));

        var bad = ThreeLines()
            .Line("20261103000000[-3:BRT]", "-5.00", "Fora do periodo")
            .Ledger("500.00", "20261031000000[-3:BRT]").Build();
        var p = await PreviewAsync(c, acc, bad);
        Assert.Equal("warning", Warning(p, "DateOutsidePeriod").GetProperty("severity").GetString());
        Assert.Equal("warning", Warning(p, "BalanceMismatch").GetProperty("severity").GetString());
        Assert.DoesNotContain("500", Warning(p, "BalanceMismatch").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Period_gap_and_overlap_with_confirmed_batches_warn()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);
        var first = await PreviewAsync(c, acc, ThreeLines().Build());
        Assert.DoesNotContain("PeriodGap", Codes(first));
        Assert.DoesNotContain("PeriodOverlap", Codes(first));

        await SeedConfirmedBatchAsync(f, c, acc, new string('a', 64), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Empty(Codes(await PreviewAsync(c, acc, ThreeLines().NoLedger().Build())));

        var gap = OfxTestFile.Checking().Period("20261110000000[-3:BRT]", "20261130000000[-3:BRT]")
            .Line("20261111000000[-3:BRT]", "-1.00", "X").NoLedger().Build();
        await SeedConfirmedBatchAsync(f, c, acc, new string('b', 64), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Contains("PeriodGap", Codes(await PreviewAsync(c, acc, gap)));

        var overlap = OfxTestFile.Checking().Period("20261015000000[-3:BRT]", "20261105000000[-3:BRT]")
            .Line("20261016000000[-3:BRT]", "-1.00", "Y").NoLedger().Build();
        var po = await PreviewAsync(c, acc, overlap);
        Assert.Equal("warning", Warning(po, "PeriodOverlap").GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Other_users_account_id_is_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var accA = await CreateAccountAsync(a);

        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(b, accA, ThreeLines().Build())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(b, Guid.NewGuid(), ThreeLines().Build())).StatusCode);
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, null);
        Assert.Equal(0, await db.ImportBatches.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Other_users_batch_is_404()
    {
        await using var f = await NewFactoryAsync();
        using var a = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        using var b = await AuthedClient.CreateAsync(f, "b@example.com", Password);
        var accA = await CreateAccountAsync(a);
        var batch = (await PreviewAsync(a, accA, ThreeLines().Build())).GetProperty("batchId").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/imports/{batch}")).StatusCode);
        Assert.Equal(0, (await JsonOf(await b.GetAsync("/api/imports"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/imports/{batch}")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        await using var f = await NewFactoryAsync();
        using var client = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/imports")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/imports/{Guid.NewGuid()}")).StatusCode);

        var token = await AuthTestHelpers.GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = Form(Guid.NewGuid()) };
        request.Headers.Add(AuthTestHelpers.AntiforgeryHeader, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Upload_without_antiforgery_is_rejected()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        var response = await c.Raw.PostAsync("/api/imports", Form(acc));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var bad = new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = Form(acc) };
        bad.Headers.Add(AuthTestHelpers.AntiforgeryHeader, "not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Raw.SendAsync(bad)).StatusCode);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        Assert.Equal(0, await db.ImportBatches.CountAsync());
    }

    [Fact]
    public async Task Malformed_upload_requests_are_400()
    {
        await using var f = await NewFactoryAsync();
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var acc = await CreateAccountAsync(c);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync("/api/imports", new { accountId = acc })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostFileAsync("/api/imports", ThreeLines().Build(), "x.ofx",
            new Dictionary<string, string> { ["accountId"] = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostFileAsync("/api/imports", ThreeLines().Build(), "x.ofx",
            new Dictionary<string, string>())).StatusCode);

        // Archived account rejects imports like it rejects manual lines.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/accounts/{acc}",
            new { name = "Conta", institution = "Banco", isArchived = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(c, acc, ThreeLines().Build())).StatusCode);
    }

    private static MultipartFormDataContent Form(Guid accountId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(ThreeLines().Build());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "extrato.ofx");
        form.Add(new StringContent(accountId.ToString()), "accountId");
        return form;
    }
}
