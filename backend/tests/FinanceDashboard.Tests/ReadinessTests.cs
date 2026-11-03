using FinanceDashboard.Tests.Infrastructure;

namespace FinanceDashboard.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ReadinessTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ready_returns_200_when_database_is_up_and_timezone_resolves()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var checks = json.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetString());
        Assert.Equal("Healthy", checks.GetProperty("timezone").GetString());
    }

    [Fact]
    public async Task Ready_returns_503_but_health_stays_200_when_database_is_down()
    {
        await using var factory = new ApiFactory(
            "Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2",
            new() { ["Database:MigrateOnStartup"] = "false" });
        using var client = factory.CreateClient();

        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health/ready")).StatusCode);
    }
}
