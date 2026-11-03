using FinanceDashboard.Api.Security;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Transactions;

public sealed record CheckpointResponse(Guid Id, Guid AccountId, DateOnly Date, decimal Amount, CheckpointOrigin Origin);

public sealed record CreateCheckpointRequest(DateOnly? Date, decimal? Amount);

public static class CheckpointEndpoints
{
    private const decimal MaxAbsAmount = 10_000_000_000_000_000m; // 10^16

    public static IEndpointRouteBuilder MapCheckpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}/checkpoints").RequireAuthorization().WithTags("Checkpoints");

        group.MapGet("/", async (Guid accountId, AppDbContext db) =>
            {
                if (!await db.Accounts.AnyAsync(a => a.Id == accountId)) return Results.NotFound();
                var items = await db.Checkpoints.AsNoTracking().Where(c => c.AccountId == accountId)
                    .OrderByDescending(c => c.Date).ThenByDescending(c => c.CreatedAt)
                    .Select(c => new CheckpointResponse(c.Id, c.AccountId, c.Date, c.Amount, c.Origin))
                    .ToListAsync();
                return Results.Ok(items);
            })
            .WithName("ListCheckpoints").WithSummary("List balance checkpoints of an account")
            .Produces<List<CheckpointResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (Guid accountId, CreateCheckpointRequest request, AppDbContext db) =>
            {
                if (request.Date is null || request.Amount is null || Math.Abs(request.Amount.Value) >= MaxAbsAmount)
                {
                    return Results.BadRequest();
                }

                if (!await db.Accounts.AnyAsync(a => a.Id == accountId)) return Results.NotFound();

                var checkpoint = new BalanceCheckpoint
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    Date = request.Date.Value,
                    Amount = request.Amount.Value,
                    Origin = CheckpointOrigin.Manual,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Checkpoints.Add(checkpoint);
                await db.SaveChangesAsync();
                return Results.Created(
                    $"/api/accounts/{accountId}/checkpoints",
                    new CheckpointResponse(checkpoint.Id, accountId, checkpoint.Date, checkpoint.Amount, checkpoint.Origin));
            })
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("CreateCheckpoint").WithSummary("Record a known balance at a date")
            .Produces<CheckpointResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
