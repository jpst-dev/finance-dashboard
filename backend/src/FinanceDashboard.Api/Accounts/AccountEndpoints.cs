using FinanceDashboard.Api.Security;
using FinanceDashboard.Api.Transactions;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Accounts;

public sealed record AccountResponse(
    Guid Id, string Name, AccountType Type, string Institution, string? ExternalIdentifier, bool IsArchived, decimal Balance);

public sealed record CreateAccountRequest(
    string? Name, AccountType? Type, string? Institution, string? ExternalIdentifier);

/// <summary>Type is immutable: when sent, it must match the stored type.</summary>
public sealed record UpdateAccountRequest(
    string? Name, AccountType? Type, string? Institution, string? ExternalIdentifier, bool? IsArchived);

public static class AccountEndpoints
{
    private const int MaxName = 80;
    private const int MaxText = 200;

    public static IEndpointRouteBuilder MapAccounts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").RequireAuthorization().WithTags("Accounts");

        group.MapGet("/", async (AppDbContext db) =>
            {
                var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
                var balances = await BalanceQueries.BalancesAsync(db, accounts.Select(a => a.Id).ToList());
                return Results.Ok(accounts.Select(a => ToResponse(a, balances[a.Id])).ToList());
            })
            .WithName("ListAccounts").WithSummary("List the user's accounts")
            .Produces<List<AccountResponse>>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
            {
                var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                return a is null ? Results.NotFound() : Results.Ok(ToResponse(a, await BalanceAsync(db, a.Id)));
            })
            .WithName("GetAccount").WithSummary("Get one account")
            .Produces<AccountResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("CreateAccount").WithSummary("Create an account")
            .Produces<AccountResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("UpdateAccount").WithSummary("Update name, institution, external identifier or archived flag")
            .Produces<AccountResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(CreateAccountRequest request, AppDbContext db)
    {
        if (!TryName(request.Name, out var name) || request.Type is null
            || !TryOptional(request.Institution, out var institution)
            || !TryOptional(request.ExternalIdentifier, out var external))
        {
            return Results.BadRequest();
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = request.Type.Value,
            Institution = institution ?? "",
            ExternalIdentifier = external,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return Results.Created($"/api/accounts/{account.Id}", ToResponse(account, 0m));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateAccountRequest request, AppDbContext db)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null) return Results.NotFound();

        if (!TryName(request.Name, out var name)
            || !TryOptional(request.Institution, out var institution)
            || !TryOptional(request.ExternalIdentifier, out var external)
            || (request.Type is not null && request.Type != account.Type))
        {
            return Results.BadRequest();
        }

        account.Name = name;
        account.Institution = institution ?? "";
        account.ExternalIdentifier = external;
        account.IsArchived = request.IsArchived ?? account.IsArchived;
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(account, await BalanceAsync(db, account.Id)));
    }

    private static bool TryName(string? raw, out string name)
    {
        name = raw?.Trim() ?? "";
        return name.Length is >= 1 and <= MaxName;
    }

    private static bool TryOptional(string? raw, out string? value)
    {
        value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return value is null || value.Length <= MaxText;
    }

    private static async Task<decimal> BalanceAsync(AppDbContext db, Guid id) =>
        (await BalanceQueries.BalancesAsync(db, [id]))[id];

    private static AccountResponse ToResponse(Account a, decimal balance) =>
        new(a.Id, a.Name, a.Type, a.Institution, a.ExternalIdentifier, a.IsArchived, balance);
}
