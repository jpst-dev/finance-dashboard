using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace FinanceDashboard.Tests.Infrastructure;

public sealed class ApiFactory(string connectionString, Dictionary<string, string?>? extraConfig = null)
    : WebApplicationFactory<Program>
{
    public string ConnectionString => connectionString;

    /// <summary>Optional extra logger provider (e.g. to capture the host's logs in a test).</summary>
    public ILoggerProvider? LoggerProvider { get; init; }

    /// <summary>Optional clock override (e.g. a FakeTimeProvider) replacing TimeProvider.System.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Every test owns a database, so every test leaves an Npgsql pool of idle connections behind; release them
    /// so the suite stays under the container's max_connections (concurrency tests open many at once).
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Database:MigrateOnStartup"] = "true",
            // Isolate from the developer's shell (README tells them to export these for `dotnet run`).
            ["SEED_USER_EMAIL"] = null,
            ["SEED_USER_PASSWORD"] = null,
        };
        if (extraConfig is not null)
        {
            foreach (var (key, value) in extraConfig) settings[key] = value;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        if (LoggerProvider is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(LoggerProvider));
        }

        if (TimeProvider is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(TimeProvider);
            });
        }
    }
}
