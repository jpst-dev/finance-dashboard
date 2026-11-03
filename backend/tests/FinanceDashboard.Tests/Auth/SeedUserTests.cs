using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FinanceDashboard.Tests.Auth.AuthTestHelpers;

namespace FinanceDashboard.Tests.Auth;

[Collection(PostgresCollection.Name)]
public sealed class SeedUserTests(PostgresFixture postgres)
{
    private const string Email = "seed.owner@example.com";
    private const string Password = "Seed-Password-42!";

    private static Dictionary<string, string?> Seed(string? email, string? password) => new()
    {
        ["SEED_USER_EMAIL"] = email,
        ["SEED_USER_PASSWORD"] = password,
    };

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        yield return ex;
        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
                foreach (var e in Flatten(inner)) yield return e;
        }
        else if (ex.InnerException is not null)
        {
            foreach (var e in Flatten(ex.InnerException)) yield return e;
        }
    }

    private static InvalidOperationException StartupFailure(ApiFactory factory)
    {
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        return Flatten(ex!).OfType<InvalidOperationException>()
            .First(e => e.Message.Contains("SEED_USER_", StringComparison.Ordinal));
    }

    private static async Task<int> CountUsersAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().Users.CountAsync();
    }

    [Fact]
    public async Task Seed_creates_the_user_once_and_login_works()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = new ApiFactory(connectionString, Seed(Email, Password)))
        {
            Assert.Equal(1, await CountUsersAsync(first));
        }

        await using var second = new ApiFactory(connectionString, Seed(Email, Password));
        Assert.Equal(1, await CountUsersAsync(second));

        var (client, _) = await LoginAsync(second, Email, Password);
        client.Dispose();
    }

    [Fact]
    public async Task Seed_with_email_but_without_password_prevents_startup()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), Seed(Email, null));

        var ex = StartupFailure(factory);

        Assert.Contains("SEED_USER_PASSWORD", ex.Message);
        Assert.DoesNotContain(Email, ex.Message);
    }

    [Fact]
    public async Task Seed_with_password_shorter_than_12_chars_prevents_startup()
    {
        const string shortPassword = "Short-1!x";
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), Seed(Email, shortPassword));

        var ex = StartupFailure(factory);

        Assert.Contains("SEED_USER_PASSWORD", ex.Message);
        Assert.DoesNotContain(shortPassword, ex.Message);
        Assert.DoesNotContain(Email, ex.Message);
    }

    [Fact]
    public async Task Seed_does_nothing_when_neither_variable_is_set()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), Seed(null, null));

        Assert.Equal(0, await CountUsersAsync(factory));
    }
}
