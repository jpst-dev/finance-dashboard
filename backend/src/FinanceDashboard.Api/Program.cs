var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/api/health", new() { Predicate = _ => false });

app.Run();

public partial class Program;
