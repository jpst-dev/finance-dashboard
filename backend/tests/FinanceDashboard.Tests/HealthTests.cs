using System.Net;
using FinanceDashboard.Tests.Infrastructure;

namespace FinanceDashboard.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_200_with_status_healthy()
    {
        // Liveness never touches the database: dummy connection string, no migration, no seed.
        await using var factory = new ApiFactory(
            "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            new() { ["Database:MigrateOnStartup"] = "false" });

        var res = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Healthy", await res.Content.ReadAsStringAsync());
    }
}
