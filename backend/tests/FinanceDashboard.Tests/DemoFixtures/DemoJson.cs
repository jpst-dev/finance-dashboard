using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FinanceDashboard.Tests.DemoFixtures;

/// <summary>Byte-stable JSON for the demo fixtures (same format as the OpenAPI snapshot).</summary>
public static class DemoJson
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Re-serializes indented, relaxed encoder, LF line endings and one trailing newline.</summary>
    public static string Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, Indented).Replace("\r\n", "\n") + "\n";
    }

    public static string Normalize(JsonNode node) => Normalize(node.ToJsonString());

    /// <summary>
    /// Orders <c>byCategory</c> by <c>categoryId</c> (ordinal), uncategorized last: the server groups in memory without
    /// an ORDER BY, so its order follows the rows the database happened to return.
    /// </summary>
    public static JsonNode SortByCategory(JsonNode summary)
    {
        var copy = summary.DeepClone();
        var byCategory = copy["byCategory"]!.AsArray();
        var items = byCategory.Select(i => i!.DeepClone())
            .OrderBy(i => i["categoryId"] is null ? 1 : 0)
            .ThenBy(i => i["categoryId"]?.GetValue<string>(), StringComparer.Ordinal)
            .ToList();
        byCategory.Clear();
        foreach (var item in items) byCategory.Add(item);
        return copy;
    }
}
