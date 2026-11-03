using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceDashboard.Tests.Infrastructure;
using static FinanceDashboard.Tests.Auth.AuthTestHelpers;

namespace FinanceDashboard.Tests.Support;

/// <summary>Logged-in client that attaches a fresh antiforgery token to every mutation.</summary>
public sealed class AuthedClient : IDisposable
{
    private readonly HttpClient _client;

    private AuthedClient(HttpClient client, string userId)
    {
        _client = client;
        UserId = userId;
    }

    public string UserId { get; }

    public HttpClient Raw => _client;

    public static async Task<AuthedClient> CreateAsync(ApiFactory f, string email, string password)
    {
        var user = await CreateUserAsync(f, email, password);
        var (client, _) = await LoginAsync(f, email, password);
        return new AuthedClient(client, user.Id);
    }

    public Task<HttpResponseMessage> GetAsync(string url, object? body = null) => SendAsync(HttpMethod.Get, url, body, false);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body, true);
    public Task<HttpResponseMessage> PutAsync(string url, object? body = null) => SendAsync(HttpMethod.Put, url, body, true);
    public Task<HttpResponseMessage> DeleteAsync(string url, object? body = null) => SendAsync(HttpMethod.Delete, url, body, true);

    public async Task<HttpResponseMessage> PostFileAsync(
        string url, byte[] content, string fileName, IDictionary<string, string> fields)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Add(AntiforgeryHeader, await GetCsrfTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, bool mutation)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        if (mutation) request.Headers.Add(AntiforgeryHeader, await GetCsrfTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    public void Dispose() => _client.Dispose();
}
