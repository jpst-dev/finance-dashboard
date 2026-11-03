using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinanceDashboard.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_200_with_status_healthy()
    {
        var res = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Healthy", await res.Content.ReadAsStringAsync());
    }
}
