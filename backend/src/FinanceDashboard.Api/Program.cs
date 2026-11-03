using System.Text.Json;
using FinanceDashboard.Api.Health;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<TimeZoneHealthCheck>("timezone");

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.MapHealthChecks("/api/health", new() { Predicate = _ => false });
app.MapHealthChecks("/api/health/ready", new()
{
    Predicate = check => check.Name is "database" or "timezone",
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    },
});

app.Run();

public partial class Program;
