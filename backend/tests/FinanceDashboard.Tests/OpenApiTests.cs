using System.Net;
using System.Text.Json;
using FinanceDashboard.Tests.Infrastructure;

namespace FinanceDashboard.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OpenApiTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OpenApi_document_lists_health_and_auth_routes()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/health", out _));
        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
        Assert.True(paths.TryGetProperty("/api/auth/me", out _));
        Assert.Equal("Auth", paths.GetProperty("/api/auth/login").GetProperty("post").GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Docs_page_is_served()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/docs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
