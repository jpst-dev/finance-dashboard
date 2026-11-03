using FinanceDashboard.Api.Links;
using FinanceDashboard.Api.Security;
using FinanceDashboard.Api.Summary;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Infrastructure.Linking;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Transactions;

public sealed record TransactionResponse(
    Guid Id, Guid AccountId, DateOnly Date, DateOnly? PurchaseDate, DateOnly? StatementDate, decimal Amount,
    string Description, string OriginalDescription, Guid? CategoryId, TransactionState State,
    TransactionOrigin Origin, bool EditedByUser, string? CounterpartyName, TransactionPairing? Pairing,
    TransactionLink? Link);

/// <summary>The pairing role of an imported line (from its bank rules), null for any other line.</summary>
public enum TransactionPairing { OwnTransfer, InvoicePayment, PixCredit }

/// <summary>
/// The Confirmed or Suggested link a line takes part in (a Confirmed one wins over suggestions). The counterpart is null
/// on an open link (the other side is outside the app).
/// </summary>
public sealed record TransactionLink(Guid Id, LinkKind Kind, LinkState State, Guid? CounterpartTransactionId);

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

        group.MapPost("/{id:guid}/own-transfer", MarkOwnAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("MarkOwnTransfer").WithSummary("Mark a transfer line as mine (open Confirmed link, no counterpart)")
            .Produces<LinkResponse>()
            .Produces<LinkErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/no-counterpart", MarkNoCounterpartAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("MarkNoCounterpart").WithSummary("Mark a pending invoice payment as having no counterpart in the app")
            .Produces<LinkResponse>()
            .Produces<LinkErrorResponse>(StatusCodes.Status422UnprocessableEntity)
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
        if (state == TransactionState.PendingClassification) query = query.WithoutConfirmedLink(db);
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
                t.OriginalDescription, t.CategoryId, t.State, t.Origin, t.EditedByUser, t.CounterpartyName, null, null))
            .ToListAsync();
        var ids = items.Select(i => i.Id).ToList();
        var links = await LinksForAsync(db, ids);
        var pairings = await PairingsForAsync(db, ids);
        items = items.Select(i => i with
        {
            Link = links.GetValueOrDefault(i.Id),
            Pairing = pairings.TryGetValue(i.Id, out var p) ? p : null,
        }).ToList();
        return Results.Ok(new TransactionPage(items, pageNo, pageSize, total));
    }

    private static Task<IResult> MarkOwnAsync(Guid id, LinkService links, AppDbContext db, CancellationToken ct) =>
        MarkAsync(links.MarkOwnAsync(id, ct), db);

    private static Task<IResult> MarkNoCounterpartAsync(Guid id, LinkService links, AppDbContext db, CancellationToken ct) =>
        MarkAsync(links.MarkNoCounterpartAsync(id, ct), db);

    // No outer transaction here: the service runs under its own lock, and a unique violation inside an ambient
    // transaction would abort it.
    private static async Task<IResult> MarkAsync(Task<LinkResult> mark, AppDbContext db)
    {
        var result = await mark;
        switch (result.Outcome)
        {
            case LinkOutcome.NotFound: return Results.NotFound();
            case LinkOutcome.Conflict: return Results.Conflict();
            case LinkOutcome.NotApplicable:
                return Results.UnprocessableEntity(new LinkErrorResponse("not_applicable"));
        }

        var view = result.LinkId is { } linkId ? await LinkEndpoints.ViewAsync(db, linkId) : null;
        return view is null ? Results.NotFound() : Results.Ok(view);
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
        return Results.Created($"/api/transactions/{transaction.Id}", await ToResponseAsync(db, transaction));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateTransactionRequest request, AppDbContext db, LinkService links)
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

        // A change to what the pairing is based on (amount, date) or the line opting out (state → Normal) drops links;
        // LinkService decides which ones per reason.
        var valueOrDate = transaction.Amount != amount || transaction.Date != request.Date.Value;
        var toNormal = request.State == TransactionState.Normal && transaction.State != TransactionState.Normal;

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

        // Edit and link invalidation commit together; LinkService joins this transaction (and takes its lock).
        await using var tx = await db.Database.BeginTransactionAsync();
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.NotFound(); } // removed by an undo meanwhile
        if (toNormal) await links.InvalidateAsync(id, InvalidateReason.StateToNormal);
        if (valueOrDate) await links.InvalidateAsync(id, InvalidateReason.ValueOrDate);
        await tx.CommitAsync();
        return Results.Ok(await ToResponseAsync(db, transaction));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, LinkService links)
    {
        var transaction = await db.Transactions.Visible().FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null) return Results.NotFound();

        if (transaction.Origin == TransactionOrigin.Manual) db.Transactions.Remove(transaction);
        else transaction.IsDeleted = true; // keeps SourceKey so a re-import does not bring the line back

        // Links cascade on a manual delete; a hidden line keeps its row, so its links are invalidated explicitly.
        await using var tx = await db.Database.BeginTransactionAsync();
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.NotFound(); } // removed by an undo meanwhile
        await links.InvalidateAsync(id, InvalidateReason.Hidden);
        await tx.CommitAsync();
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

    private static async Task<TransactionResponse> ToResponseAsync(AppDbContext db, Transaction t)
    {
        var links = await LinksForAsync(db, [t.Id]);
        var pairings = await PairingsForAsync(db, [t.Id]);
        return ToResponse(t) with
        {
            Link = links.GetValueOrDefault(t.Id),
            Pairing = pairings.TryGetValue(t.Id, out var p) ? p : null,
        };
    }

    /// <summary>One query for a whole page: Confirmed/Suggested links of the given lines (Confirmed first).</summary>
    private static async Task<Dictionary<Guid, TransactionLink>> LinksForAsync(AppDbContext db, List<Guid> ids)
    {
        var result = new Dictionary<Guid, TransactionLink>();
        if (ids.Count == 0) return result;

        var rows = await db.Links.AsNoTracking()
            .Where(l => (l.State == LinkState.Confirmed || l.State == LinkState.Suggested)
                && ((l.OutTransactionId != null && ids.Contains(l.OutTransactionId.Value))
                    || (l.InTransactionId != null && ids.Contains(l.InTransactionId.Value))))
            .OrderBy(l => l.State == LinkState.Confirmed ? 0 : 1).ThenBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .ToListAsync();
        var wanted = ids.ToHashSet();
        foreach (var l in rows)
        {
            if (l.OutTransactionId is { } outId && wanted.Contains(outId))
                result.TryAdd(outId, new TransactionLink(l.Id, l.Kind, l.State, l.InTransactionId));
            if (l.InTransactionId is { } inId && wanted.Contains(inId))
                result.TryAdd(inId, new TransactionLink(l.Id, l.Kind, l.State, l.OutTransactionId));
        }

        return result;
    }

    /// <summary>
    /// One query for a whole page: the pairing role of imported lines (batch Source join, bank rules in memory).
    /// Manual lines have no batch and so no role.
    /// </summary>
    private static async Task<Dictionary<Guid, TransactionPairing>> PairingsForAsync(AppDbContext db, List<Guid> ids)
    {
        var result = new Dictionary<Guid, TransactionPairing>();
        if (ids.Count == 0) return result;

        var rows = await (
                from t in db.Transactions.AsNoTracking().Visible()
                where ids.Contains(t.Id)
                join b in db.ImportBatches.AsNoTracking() on t.BatchId equals b.Id
                select new { t.Id, b.Source, t.OriginalDescription, t.Amount })
            .ToListAsync();
        foreach (var r in rows)
        {
            TransactionPairing? pairing = PairingRules.RoleOf(r.Source, r.OriginalDescription, r.Amount) switch
            {
                PairingRole.Transfer => TransactionPairing.OwnTransfer,
                PairingRole.InvoiceAccountSide or PairingRole.InvoiceCardSide => TransactionPairing.InvoicePayment,
                PairingRole.PixCreditCredit or PairingRole.PixCreditDebit => TransactionPairing.PixCredit,
                _ => null,
            };
            if (pairing is { } p) result[r.Id] = p;
        }

        return result;
    }

    private static TransactionResponse ToResponse(Transaction t) => new(
        t.Id, t.AccountId, t.Date, t.PurchaseDate, t.StatementDate, t.Amount, t.NormalizedDescription,
        t.OriginalDescription, t.CategoryId, t.State, t.Origin, t.EditedByUser, t.CounterpartyName, null, null);
}
