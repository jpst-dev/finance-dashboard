using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinanceDashboard.Tests.Infrastructure;

public sealed class ApiFactory(string connectionString, Dictionary<string, string?>? extraConfig = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Optional extra logger provider (e.g. to capture the host's logs in a test).</summary>
    public ILoggerProvider? LoggerProvider { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Database:MigrateOnStartup"] = "true",
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
