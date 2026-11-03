using System.Text.Json.Nodes;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using static FinanceDashboard.Tests.Auth.AuthTestHelpers;

namespace FinanceDashboard.Tests.DemoFixtures;

/// <summary>
/// Seeds a fresh database with <see cref="DemoSeed"/>, calls the real API as the demo visitor under a fixed clock and
/// returns the demo fixtures (file name → normalized JSON). Balances, summaries and lists are the server's answers;
/// the only number computed here is in the seed (the preview's declared ledger balance, as a bank file would carry it).
/// </summary>
public sealed class DemoFixtureGenerator
{
    public const string Generator = "finance-demo-fixtures";
    private const int PageSize = 200;

    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    public static async Task<IReadOnlyDictionary<string, string>> GenerateAsync(PostgresFixture pg)
    {
        await using var f = new ApiFactory(await pg.CreateDatabaseAsync()) { TimeProvider = new FakeTimeProvider(Now) };
        // Requests run in the caller's execution context, so a test's CurrentCulture reaches the server's serialization.
        f.Server.PreserveExecutionContext = true;
        await DemoSeed.SeedAsync(f);
        // The session cookie expires 7 days after the fake clock, i.e. already in the past for the real clock, so
        // HttpClient's cookie container would drop it: send it by hand (the server validates it with the fake clock).
        var (login, cookie) = await LoginAsync(f, DemoSeed.Email, DemoSeed.Password);
        login.Dispose();
        using var client = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        async Task<JsonNode> Get(string url)
        {
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GET {url} answered {(int)response.StatusCode}.");
            }

            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Put(string name, JsonNode node) => files[name] = DemoJson.Normalize(node);

        Put("meta.json", new JsonObject
        {
            ["generator"] = Generator,
            ["referenceDate"] = "2026-10-15",
            ["months"] = new JsonArray(DemoSeed.Months.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["previewBatchId"] = DemoSeed.PreviewBatchId.ToString(),
            ["user"] = await Get("/api/auth/me"),
        });

        var accounts = await Get("/api/accounts");
        Put("accounts.json", accounts);

        var checkpoints = new JsonObject();
        var accountIds = accounts.AsArray().Select(a => a!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        foreach (var id in accountIds) checkpoints[id] = await Get($"/api/accounts/{id}/checkpoints");
        Put("checkpoints.json", checkpoints);

        Put("categories.json", await Get("/api/categories"));

        var transactions = new JsonArray();
        for (var page = 1; ; page++)
        {
            var body = await Get($"/api/transactions?page={page}&size={PageSize}");
            var items = body["items"]!.AsArray();
            foreach (var item in items) transactions.Add(item!.DeepClone());
            if (items.Count == 0 || transactions.Count >= body["total"]!.GetValue<int>()) break;
        }
        Put("transactions.json", transactions);

        var summaries = new JsonObject();
        foreach (var month in DemoSeed.Months) summaries[month] = DemoJson.SortByCategory(await Get($"/api/summary?month={month}"));
        Put("summaries.json", summaries);

        var empty = await Get("/api/summary?month=2026-04");
        if (empty["income"]!.GetValue<string>() != "0.00" || empty["expenses"]!.GetValue<string>() != "0.00"
            || empty["net"]!.GetValue<string>() != "0.00" || empty["byCategory"]!.AsArray().Count != 0
            || empty["pending"]!["count"]!.GetValue<int>() != 0)
        {
            throw new InvalidOperationException("The month before the demo range is not empty.");
        }
        Put("summary-empty.json", empty);

        var imports = await Get("/api/imports");
        Put("imports.json", imports);

        var details = new JsonObject();
        var batchIds = imports.AsArray().Select(b => b!["batchId"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        foreach (var id in batchIds) details[id] = await Get($"/api/imports/{id}");
        Put("import-details.json", details);

        return files;
    }

    /// <summary><c>src/demo-static/fixtures</c> at the repository root (one level above <c>FinanceDashboard.sln</c>).</summary>
    public static string FixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinanceDashboard.sln"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("FinanceDashboard.sln not found above the test output.");
        return Path.GetFullPath(Path.Combine(dir.FullName, "..", "src", "demo-static", "fixtures"));
    }
}
