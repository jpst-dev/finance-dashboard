using System.Net;
using System.Net.Http.Json;
using FinanceDashboard.Tests.Infrastructure;
using static FinanceDashboard.Tests.Auth.AuthTestHelpers;

namespace FinanceDashboard.Tests.Auth;

[Collection(PostgresCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres)
{
    private const string Email = "user@example.com";
    private const string Password = "Correct-Horse-42!";
    private const string WrongPassword = "Wrong-Battery-17?";

    private async Task<ApiFactory> NewFactoryAsync(Dictionary<string, string?>? extraConfig = null, CapturingLoggerProvider? logs = null) =>
        new(await postgres.CreateDatabaseAsync(), extraConfig) { LoggerProvider = logs };

    [Fact]
    public async Task Login_with_valid_credentials_sets_httponly_cookie_and_me_returns_email()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUserAsync(factory, Email, Password);
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, Email, Password, await GetCsrfTokenAsync(client));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = GetSessionSetCookie(response).ToLowerInvariant();
        Assert.Contains("httponly", setCookie);
        Assert.Contains("samesite=lax", setCookie);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(Email, body?.Email);
    }

    [Fact]
    public async Task Login_normalizes_email_case_and_surrounding_spaces()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUserAsync(factory, Email, Password);
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, "  USER@Example.COM ", Password, await GetCsrfTokenAsync(client));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_same_401_for_unknown_email_and_wrong_password()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUserAsync(factory, Email, Password);
        using var client = factory.CreateClient();
        var token = await GetCsrfTokenAsync(client);

        var unknown = await PostLoginAsync(client, "nobody@example.com", Password, token);
        var wrong = await PostLoginAsync(client, Email, WrongPassword, token);

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        Assert.Equal(unknown.Content.Headers.ContentType?.ToString(), wrong.Content.Headers.ContentType?.ToString());
        Assert.DoesNotContain(GetSetCookies(unknown), IsSession);
        Assert.DoesNotContain(GetSetCookies(wrong), IsSession);
    }

    [Fact]
    public async Task Login_is_rate_limited_after_five_attempts_in_a_minute()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUserAsync(factory, Email, Password);
        using var client = factory.CreateClient();
        var token = await GetCsrfTokenAsync(client);

        for (var i = 0; i < 5; i++)
        {
            var attempt = await PostLoginAsync(client, Email, WrongPassword, token);
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var sixth = await PostLoginAsync(client, Email, Password, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUserAsync(factory, Email, Password);
        using var client = factory.CreateClient();
        await GetCsrfTokenAsync(client); // antiforgery cookie present, header missing or forged

        var missing = await PostLoginAsync(client, Email, Password, csrfToken: null);
        var forged = await PostLoginAsync(client, Email, Password, csrfToken: "not-a-valid-token");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_without_cookie_returns_401()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Old_cookie_stops_working_after_logout()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUserAsync(factory, Email, Password);
        var (client, cookie) = await LoginAsync(factory, Email, Password);
        using var copy = factory.CreateClient(new() { HandleCookies = false });

        Assert.Equal(HttpStatusCode.OK, (await GetMeWithCookieAsync(copy, cookie)).StatusCode);

        var logout = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeWithCookieAsync(copy, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Failed_login_does_not_log_email_or_password()
    {
        var logs = new CapturingLoggerProvider();
        // Most verbose levels on purpose: nothing at any level may carry the submitted credentials.
        await using var factory = await NewFactoryAsync(
            new() { ["Logging:LogLevel:Default"] = "Trace", ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace" },
            logs);
        await CreateUserAsync(factory, Email, Password);
        using var client = factory.CreateClient();
        var token = await GetCsrfTokenAsync(client);
        const string unknownEmail = "someone-else@example.com";

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostLoginAsync(client, Email, WrongPassword, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostLoginAsync(client, unknownEmail, WrongPassword, token)).StatusCode);

        var entries = logs.Entries;
        Assert.NotEmpty(entries);
        foreach (var secret in new[] { Email, unknownEmail, WrongPassword })
        {
            Assert.DoesNotContain(entries, e => e.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task<HttpResponseMessage> GetMeWithCookieAsync(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static IEnumerable<string> GetSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private static bool IsSession(string setCookie) => setCookie.StartsWith(SessionCookieName + "=", StringComparison.Ordinal);

    private sealed record MeResponse(string Email);
}
