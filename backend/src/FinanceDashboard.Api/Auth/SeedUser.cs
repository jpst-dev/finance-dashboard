using System.Net.Mail;
using FinanceDashboard.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace FinanceDashboard.Api.Auth;

/// <summary>
/// Creates the single initial user from SEED_USER_EMAIL / SEED_USER_PASSWORD (there is no open signup).
/// Idempotent and fail-fast. Error messages never include the e-mail or the password.
/// </summary>
public static class SeedUser
{
    private const string EmailKey = "SEED_USER_EMAIL";
    private const string PasswordKey = "SEED_USER_PASSWORD";
    private const int MinPasswordLength = 12;

    public static async Task EnsureAsync(IServiceProvider sp, IConfiguration config, CancellationToken ct = default)
    {
        var email = config[EmailKey]?.Trim();
        var password = config[PasswordKey];
        var hasEmail = !string.IsNullOrEmpty(email);
        var hasPassword = !string.IsNullOrEmpty(password);

        if (!hasEmail && !hasPassword) return;

        if (!hasEmail)
            throw new InvalidOperationException($"{PasswordKey} is set but {EmailKey} is missing or empty.");
        if (!hasPassword)
            throw new InvalidOperationException($"{EmailKey} is set but {PasswordKey} is missing or empty.");
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new InvalidOperationException($"{EmailKey} is not a valid e-mail address.");
        if (password!.Length < MinPasswordLength)
            throw new InvalidOperationException($"{PasswordKey} must have at least {MinPasswordLength} characters.");

        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        ct.ThrowIfCancellationRequested();

        if (await users.FindByEmailAsync(email!) is not null) return;

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the seed user from SEED_USER_EMAIL/SEED_USER_PASSWORD: "
                + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SeedUser).FullName!)
            .LogInformation("seed user created");
    }
}
