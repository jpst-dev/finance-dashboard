using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinanceDashboard.Tests.Infrastructure;

public sealed class ApiFactory(string connectionString, Dictionary<string, string?>? extraConfig = null)
    : WebApplicationFactory<Program>
{
    public string ConnectionString => connectionString;

    /// <summary>Optional extra logger provider (e.g. to capture the host's logs in a test).</summary>
    public ILoggerProvider? LoggerProvider { get; init; }

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
    }
}
