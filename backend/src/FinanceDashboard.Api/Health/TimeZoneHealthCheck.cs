using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinanceDashboard.Api.Health;

public sealed class TimeZoneHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (TimeZoneNotFoundException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("America/Sao_Paulo not found"));
        }
    }
}
