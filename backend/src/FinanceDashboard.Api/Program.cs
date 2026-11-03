using System.Text.Json;
using FinanceDashboard.Api.Accounts;
using FinanceDashboard.Api.Auth;
using FinanceDashboard.Api.Categories;
using FinanceDashboard.Api.CategoryRules;
using FinanceDashboard.Api.Json;
using FinanceDashboard.Api.Health;
using FinanceDashboard.Api.Imports;
using FinanceDashboard.Api.Links;
using FinanceDashboard.Api.Security;
using FinanceDashboard.Api.Summary;
using FinanceDashboard.Api.Transactions;
using FinanceDashboard.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

// nginx fronts the API: trust X-Forwarded-* only from private (Docker network) ranges,
// so the login rate limiter keys on the real client IP.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var range in new[] { "172.16.0.0/12", "10.0.0.0/8", "192.168.0.0/16" })
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(range));
    }
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuth();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new DecimalStringConverter());
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddCheck<TimeZoneHealthCheck>("timezone");

// Health checks are middleware-style endpoints, invisible to API Explorer: describe them by hand.
builder.Services.AddOpenApi(options =>
{
    // The wire contract: money is a decimal STRING (DecimalStringConverter), enums are string names.
    options.AddSchemaTransformer((schema, context, _) =>
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type == typeof(decimal))
        {
            var nullable = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) is not null
                || (schema.Type is { } t && t.HasFlag(JsonSchemaType.Null));
            schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
            schema.Format = null;
            schema.Pattern = @"^-?[0-9]{1,16}(\.[0-9]{1,2})?$";
        }
        else if (type.IsEnum)
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = null;
            schema.Enum = Enum.GetNames(type).Select(n => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(n)!).ToList();
        }
        return Task.CompletedTask;
    });
    options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Finance Dashboard API";
    document.Paths ??= [];
    document.Paths["/api/health"] = HealthPath("HealthLive", "Liveness: the process is up");
    document.Paths["/api/health/ready"] = HealthPath("HealthReady", "Readiness: database and time zone are available", unhealthy: true);
    return Task.CompletedTask;

    static OpenApiPathItem HealthPath(string name, string summary, bool unhealthy = false)
    {
        var responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "Healthy" } };
        if (unhealthy) responses["503"] = new OpenApiResponse { Description = "Unhealthy" };
        return new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new()
                {
                    OperationId = name,
                    Summary = summary,
                    Tags = new HashSet<OpenApiTagReference> { new("Health") },
                    Responses = responses,
                },
            },
        };
    }
});
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

await SeedUser.EnsureAsync(app.Services, app.Configuration);
// Needs the schema; liveness/readiness setups run without a database (MigrateOnStartup=false).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await DefaultCategories.EnsureForAllUsersAsync(app.Services);
}

app.UseForwardedHeaders();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuth();
app.MapAccounts();
app.MapCategories();
app.MapTransactions();
app.MapCheckpoints();
app.MapSummary();
app.MapImports();
app.MapLinks();
app.MapCategoryRules();

// API docs for developers and reviewers: served in every environment, no authentication.
app.MapOpenApi("/api/openapi/{documentName}.json");
app.MapScalarApiReference("/api/docs", options => options
    .WithTitle("Finance Dashboard API")
    .WithOpenApiRoutePattern("/api/openapi/{documentName}.json"));

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
