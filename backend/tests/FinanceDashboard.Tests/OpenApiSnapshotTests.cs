using System.Text.Encodings.Web;
using System.Text.Json;
using FinanceDashboard.Tests.Infrastructure;

namespace FinanceDashboard.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OpenApiSnapshotTests(PostgresFixture postgres)
{
    private const string MoneyPattern = @"^-?[0-9]{1,16}(\.[0-9]{1,2})?$";
    private static readonly string[] MoneyProperties = ["amount", "balance", "total", "income", "expenses", "net"];

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private async Task<string> GetDocumentAsync()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();
        return await client.GetStringAsync("/api/openapi/v1.json");
    }

    private static string Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, Indented).Replace("\r\n", "\n") + "\n";
    }

    private static string SnapshotPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinanceDashboard.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "openapi", "v1.json");
    }

    private static IEnumerable<(string Name, JsonElement Schema)> Properties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (p.Name == "properties" && p.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in p.Value.EnumerateObject()) yield return (prop.Name, prop.Value);
                }
                foreach (var nested in Properties(p.Value)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in Properties(item)) yield return nested;
            }
        }
    }

    private static bool HasType(JsonElement schema, string type)
    {
        if (!schema.TryGetProperty("type", out var t)) return false;
        return t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Any(x => x.GetString() == type)
            : t.GetString() == type;
    }

    [Fact]
    public async Task Decimals_are_documented_as_strings_with_money_pattern()
    {
        using var doc = JsonDocument.Parse(await GetDocumentAsync());
        var money = Properties(doc.RootElement).Where(p => MoneyProperties.Contains(p.Name) && !HasType(p.Schema, "integer")).ToList();

        Assert.NotEmpty(money);
        foreach (var (name, schema) in money)
        {
            Assert.False(HasType(schema, "number"), $"{name} is documented as a number");
            Assert.True(HasType(schema, "string"), $"{name} is not a string");
            Assert.Equal(MoneyPattern, schema.GetProperty("pattern").GetString());
        }
    }

    [Fact]
    public async Task Enums_are_documented_as_strings()
    {
        using var doc = JsonDocument.Parse(await GetDocumentAsync());
        var enums = new List<JsonElement>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array) enums.Add(e);
                foreach (var p in e.EnumerateObject()) Walk(p.Value);
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in e.EnumerateArray()) Walk(i);
            }
        }
        Walk(doc.RootElement);

        Assert.NotEmpty(enums);
        foreach (var schema in enums)
        {
            Assert.True(HasType(schema, "string"), "enum is not documented as a string");
            Assert.All(schema.GetProperty("enum").EnumerateArray(), v => Assert.Equal(JsonValueKind.String, v.ValueKind));
        }
    }

    [Fact]
    public async Task Committed_snapshot_matches_generated_document()
    {
        var generated = Normalize(await GetDocumentAsync());
        var path = SnapshotPath();

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated);
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot missing: run with UPDATE_OPENAPI_SNAPSHOT=1 ({path})");
        var committed = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
        Assert.Equal(generated, committed);
    }
}
