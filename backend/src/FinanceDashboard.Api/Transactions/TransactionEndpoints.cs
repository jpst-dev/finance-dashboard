using FinanceDashboard.Api.Security;
using FinanceDashboard.Api.Summary;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Transactions;

public sealed record TransactionResponse(
    Guid Id, Guid AccountId, DateOnly Date, DateOnly? PurchaseDate, DateOnly? StatementDate, decimal Amount,
    string Description, string OriginalDescription, Guid? CategoryId, TransactionState State,
    TransactionOrigin Origin, bool EditedByUser, string? CounterpartyName);

public sealed record TransactionPage(IReadOnlyList<TransactionResponse> Items, int Page, int Size, int Total);

public sealed record CreateTransactionRequest(
    Guid? AccountId, DateOnly? Date, decimal? Amount, string? Description, Guid? CategoryId, TransactionState? State);

/// <summary>Full replace of the editable fields; a null category clears it, a null state keeps the current one.</summary>
public sealed record UpdateTransactionRequest(
    DateOnly? Date, decimal? Amount, string? Description, Guid? CategoryId, TransactionState? State);

public static class TransactionEndpoints
{
    private const int MaxDescription = 300;
    private const int MaxPageSize = 200;
    private const int MaxPage = 1_000_000;
    private const decimal MaxAbsAmount = 10_000_000_000_000_000m; // 10^16

    public static IEndpointRouteBuilder MapTransactions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").RequireAuthorization().WithTags("Transactions");

        group.MapGet("/", ListAsync)
            .WithName("ListTransactions").WithSummary("List visible transactions (date desc, id desc), paginated")
            .Produces<TransactionPage>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("CreateTransaction").WithSummary("Create a manual transaction")
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("UpdateTransaction").WithSummary("Edit a transaction (imported lines are flagged as edited)")
            .Produces<TransactionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("DeleteTransaction").WithSummary("Delete a manual line, or hide an imported one (key kept)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db, Guid? accountId, string? month, Guid? categoryId, TransactionState? state,
        int? page, int? size)
    {
        var pageNo = page ?? 1;
        var pageSize = Math.Min(size ?? 50, MaxPageSize);
        if (pageNo < 1 || pageNo > MaxPage || pageSize < 1) return Results.BadRequest();

        var query = db.Transactions.AsNoTracking().Visible();
        if (accountId is not null) query = query.Where(t => t.AccountId == accountId);
        if (categoryId is not null) query = query.Where(t => t.CategoryId == categoryId);
        if (state is not null) query = query.Where(t => t.State == state);
        if (month is not null)
        {
            if (!MonthRange.TryParse(month, out var start, out var end)) return Results.BadRequest();
            query = query.Where(t => t.Date >= start && t.Date < end);
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip((int)Math.Min((long)(pageNo - 1) * pageSize, int.MaxValue)).Take(pageSize)
            .Select(t => new TransactionResponse(
                t.Id, t.AccountId, t.Date, t.PurchaseDate, t.StatementDate, t.Amount, t.NormalizedDescription,
                t.OriginalDescription, t.CategoryId, t.State, t.Origin, t.EditedByUser, t.CounterpartyName))
            .ToListAsync();
        return Results.Ok(new TransactionPage(items, pageNo, pageSize, total));
    }

    private static async Task<IResult> CreateAsync(CreateTransactionRequest request, AppDbContext db)
    {
        if (request.AccountId is null || request.Date is null
            || !TryAmount(request.Amount, out var amount) || !TryDescription(request.Description, out var description))
        {
            return Results.BadRequest();
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId);
        if (account is null) return Results.NotFound();
        if (account.IsArchived) return Results.Conflict();
        if (request.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == request.CategoryId))
        {
            return Results.NotFound();
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Date = request.Date.Value,
            Amount = amount,
            OriginalDescription = description,
            NormalizedDescription = description,
            CategoryId = request.CategoryId,
            State = request.State ?? TransactionState.Normal,
            Origin = TransactionOrigin.Manual,
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return Results.Created($"/api/transactions/{transaction.Id}", ToResponse(transaction));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateTransactionRequest request, AppDbContext db)
    {
        var transaction = await db.Transactions.Visible().FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null) return Results.NotFound();

        if (request.Date is null || !TryAmount(request.Amount, out var amount)
            || !TryDescription(request.Description, out var description))
        {
            return Results.BadRequest();
        }

        if (request.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == request.CategoryId))
        {
            return Results.NotFound();
        }

        transaction.Date = request.Date.Value;
        transaction.Amount = amount;
        transaction.NormalizedDescription = description;
        transaction.CategoryId = request.CategoryId;
        transaction.State = request.State ?? transaction.State;
        if (transaction.Origin == TransactionOrigin.Manual)
        {
            transaction.OriginalDescription = description;
        }
        else
        {
            transaction.EditedByUser = true; // SourceKey, Ordinal, BatchId, Origin and the original text stay as imported.
        }

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.NotFound(); } // removed by an undo meanwhile
        return Results.Ok(ToResponse(transaction));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var transaction = await db.Transactions.Visible().FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null) return Results.NotFound();

        if (transaction.Origin == TransactionOrigin.Manual) db.Transactions.Remove(transaction);
        else transaction.IsDeleted = true; // keeps SourceKey so a re-import does not bring the line back

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.NotFound(); } // removed by an undo meanwhile
        return Results.NoContent();
    }

    private static bool TryAmount(decimal? raw, out decimal amount)
    {
        amount = raw ?? 0m;
        return raw is not null && amount != 0m && Math.Abs(amount) < MaxAbsAmount;
    }

    private static bool TryDescription(string? raw, out string description)
    {
        description = raw?.Trim() ?? "";
        return description.Length is >= 1 and <= MaxDescription;
    }

    private static TransactionResponse ToResponse(Transaction t) => new(
        t.Id, t.AccountId, t.Date, t.PurchaseDate, t.StatementDate, t.Amount, t.NormalizedDescription,
        t.OriginalDescription, t.CategoryId, t.State, t.Origin, t.EditedByUser, t.CounterpartyName);
}
