using System.Security.Claims;
using System.Threading.RateLimiting;
using FinanceDashboard.Api.Security;
using FinanceDashboard.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FinanceDashboard.Api.Auth;

public static class AuthEndpoints
{
    public const string SessionCookieName = "fd.session";
    public const string AntiforgeryCookieName = "fd.xsrf";
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";
    public const string LoginRateLimitPolicy = "login";

    private const long MaxAuthRequestBodyBytes = 8 * 1024;

    // Used only to spend the same hashing time when the e-mail is unknown.
    private static readonly AppUser TimingDummyUser = new();

    public static IServiceCollection AddAuth(this IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.User.RequireUniqueEmail = true;
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(o =>
            {
                o.Cookie.Name = SessionCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(7);
                o.SlidingExpiration = true;
                // API: answer with status codes, never redirect to a login page.
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            }));

        // Re-check the security stamp on every request, so logout invalidates copied cookies at once.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        services.AddAuthorization();

        services.AddAntiforgery(o =>
        {
            o.HeaderName = AntiforgeryHeaderName;
            o.Cookie.Name = AntiforgeryCookieName;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithMetadata(new RequestSizeLimitAttribute(MaxAuthRequestBodyBytes))
            .WithTags("Auth");

        // Antiforgery tokens are bound to the caller's identity: clients must fetch a new token after login.
        group.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken }))
            .WithName("GetCsrfToken").WithSummary("Issue an antiforgery token (send it in X-XSRF-TOKEN)");

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("Login").WithSummary("Sign in with e-mail and password; sets the session cookie")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", LogoutAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("Logout").WithSummary("Sign out and invalidate the session")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/me", (ClaimsPrincipal user) =>
                Results.Ok(new { email = user.FindFirstValue(ClaimTypes.Email) }))
            .RequireAuthorization()
            .WithName("GetCurrentUser").WithSummary("E-mail of the signed-in user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var user = email.Length == 0 ? null : await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Spend the same hashing cost as a real check so timing does not reveal whether the e-mail exists.
            users.PasswordHasher.HashPassword(TimingDummyUser, password);
            return InvalidCredentials();
        }

        // No lockout: the rate limit is the protection, and lockout would let anyone lock out the only user.
        var result = await signIn.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: false);
        return result.Succeeded ? Results.Ok() : InvalidCredentials();
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null)
        {
            // Rotating the stamp makes any copy of the session cookie stop working.
            await users.UpdateSecurityStampAsync(user);
        }

        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    // Same status and body for unknown e-mail and wrong password.
    private static IResult InvalidCredentials() =>
        Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
}
