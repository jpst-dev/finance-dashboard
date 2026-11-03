using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceDashboard.Tests.Auth;

public static class AuthTestHelpers
{
    public const string SessionCookieName = "fd.session";
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    public static async Task<AppUser> CreateUserAsync(ApiFactory f, string email, string password)
    {
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create test user: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        return user;
    }

    public static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>();
        return body?.Token ?? throw new InvalidOperationException("Missing antiforgery token.");
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string password, string? csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        if (csrfToken is not null) request.Headers.Add(AntiforgeryHeader, csrfToken);
        return await client.SendAsync(request);
    }

    public static async Task<(HttpClient client, string cookie)> LoginAsync(ApiFactory f, string email, string password)
    {
        var client = f.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        var response = await PostLoginAsync(client, email, password, token);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Login failed with {(int)response.StatusCode}.");
        }

        return (client, GetSessionSetCookie(response).Split(';')[0]);
    }

    public static async Task<HttpResponseMessage> LogoutAsync(HttpClient client)
    {
        // Antiforgery tokens are bound to the caller's identity: fetch a new one after login.
        var token = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add(AntiforgeryHeader, token);
        return await client.SendAsync(request);
    }

    public static string GetSessionSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Single(v => v.StartsWith(SessionCookieName + "=", StringComparison.Ordinal))
            : throw new InvalidOperationException("No Set-Cookie header in response.");

    private sealed record CsrfResponse(string Token);
}

/// <summary>Captures every log entry the host writes (message, state and exception) for assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            entries.Enqueue($"{category} scope: {Describe(state)}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue($"{category} [{logLevel}] {formatter(state, exception)} | {Describe(state)} | {exception}");
        }

        private static string Describe(object? state) =>
            state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"))
                : state?.ToString() ?? string.Empty;
    }
}
