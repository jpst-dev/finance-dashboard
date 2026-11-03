using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Summary;

public sealed record CategoryTotalResponse(Guid? CategoryId, string? CategoryName, decimal Total);

public sealed record PendingSummary(int Count, decimal Total);

public sealed record SummaryResponse(
    string Month, decimal Income, decimal Expenses, decimal Net,
    IReadOnlyList<CategoryTotalResponse> ByCategory, PendingSummary Pending);

public static class SummaryEndpoints
{
    public static IEndpointRouteBuilder MapSummary(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/summary").RequireAuthorization().WithTags("Summary");

        group.MapGet("/", GetAsync)
            .WithName("GetMonthlySummary").WithSummary("Monthly summary computed on the server (visible lines only)")
            .Produces<SummaryResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAsync(AppDbContext db, string? month, Guid? accountId)
    {
        if (!MonthRange.TryParse(month, out var start, out var end)) return Results.BadRequest();

        if (accountId is not null && !await db.Accounts.AnyAsync(a => a.Id == accountId))
        {
            return Results.NotFound();
        }

        // Pending lines already on a Confirmed link are not "to classify"; everything else counts as before.
        var query = db.Transactions.AsNoTracking().Visible().Where(t => t.Date >= start && t.Date < end)
            .ExcludingConfirmedPending(db);
        if (accountId is not null) query = query.Where(t => t.AccountId == accountId);

        var lines = await query
            .Join(db.Accounts, t => t.AccountId, a => a.Id,
                (t, a) => new SummaryLine(t.Amount, a.Type, t.State, t.CategoryId))
            .ToListAsync();

        var result = MonthlySummaryCalculator.Compute(lines);

        var ids = result.ByCategory.Where(c => c.CategoryId is not null).Select(c => c.CategoryId!.Value).ToList();
        var names = await db.Categories.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var byCategory = result.ByCategory
            .Select(c => new CategoryTotalResponse(
                c.CategoryId, c.CategoryId is { } id && names.TryGetValue(id, out var n) ? n : null, c.Total))
            .ToList();

        return Results.Ok(new SummaryResponse(
            month!, result.Income, result.Expenses, result.Net, byCategory,
            new PendingSummary(result.PendingCount, result.PendingTotal)));
    }
}
