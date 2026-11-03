using FinanceDashboard.Api.Security;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Infrastructure.Linking;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Links;

public sealed record LinkLine(Guid TransactionId, Guid AccountId, DateOnly Date, string Description, decimal Amount);

public sealed record LinkResponse(
    Guid Id, LinkKind Kind, LinkState State, LinkOrigin Origin, LinkLine Out, LinkLine In, DateTimeOffset CreatedAt);

public sealed record LinkPage(IReadOnlyList<LinkResponse> Items, int Page, int Size, int Total);

/// <summary>
/// HTTP shape of <c>POST /api/links/reconcile</c>, kept as in S3(a) for now (it shadows the service's
/// <see cref="Infrastructure.Linking.ReconcileResult"/> inside this namespace). The S3(b) API task adds
/// <c>reclassified</c> together with the OpenAPI snapshot.
/// </summary>
public sealed record ReconcileResult(int Linked, int Suggested);

public static class LinkEndpoints
{
    private const int MaxPageSize = 200;
    private const int MaxPage = 1_000_000;

    public static IEndpointRouteBuilder MapLinks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/links").RequireAuthorization().WithTags("Links");

        group.MapGet("/", ListAsync)
            .WithName("ListLinks").WithSummary("List links (newest first), optionally by state, paginated")
            .Produces<LinkPage>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/reconcile", ReconcileAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("ReconcileLinks").WithSummary("Pair pending lines again (idempotent)")
            .Produces<ReconcileResult>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/confirm", ConfirmAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("ConfirmLink").WithSummary("Confirm a suggested link")
            .Produces<LinkResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/reject", RejectAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("RejectLink").WithSummary("Reject a suggested link (it will not be suggested again)")
            .Produces<LinkResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", UndoAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("UndoLink").WithSummary("Undo a confirmed link (kept as rejected so it is not paired again)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static IQueryable<LinkResponse> Project(AppDbContext db, IQueryable<Link> links) =>
        from l in links
        join o in db.Transactions.AsNoTracking() on l.OutTransactionId equals o.Id
        join i in db.Transactions.AsNoTracking() on l.InTransactionId equals i.Id
        select new LinkResponse(
            l.Id, l.Kind, l.State, l.Origin,
            new LinkLine(o.Id, o.AccountId, o.Date, o.NormalizedDescription, o.Amount),
            new LinkLine(i.Id, i.AccountId, i.Date, i.NormalizedDescription, i.Amount),
            l.CreatedAt);

    private static async Task<IResult> ListAsync(AppDbContext db, LinkState? state, int? page, int? size)
    {
        var pageNo = page ?? 1;
        var pageSize = Math.Min(size ?? 50, MaxPageSize);
        if (pageNo < 1 || pageNo > MaxPage || pageSize < 1) return Results.BadRequest();

        var links = db.Links.AsNoTracking();
        if (state is not null) links = links.Where(l => l.State == state);

        var total = await links.CountAsync();
        var items = await Project(db, links.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
                .Skip((int)Math.Min((long)(pageNo - 1) * pageSize, int.MaxValue)).Take(pageSize))
            .ToListAsync();
        // Project() re-joins, which does not keep the page order: restore it.
        items = items.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).ToList();
        return Results.Ok(new LinkPage(items, pageNo, pageSize, total));
    }

    private static async Task<IResult> ReconcileAsync(LinkService links, CancellationToken ct)
    {
        var result = await links.ReconcileAsync(ct);
        return Results.Ok(new ReconcileResult(result.Linked, result.Suggested));
    }

    private static Task<IResult> ConfirmAsync(Guid id, LinkService links, AppDbContext db, CancellationToken ct) =>
        DecideAsync(id, links.ConfirmAsync(id, ct), db);

    private static Task<IResult> RejectAsync(Guid id, LinkService links, AppDbContext db, CancellationToken ct) =>
        DecideAsync(id, links.RejectAsync(id, ct), db);

    private static async Task<IResult> DecideAsync(Guid id, Task<LinkOutcome> outcome, AppDbContext db)
    {
        switch (await outcome)
        {
            case LinkOutcome.NotFound: return Results.NotFound();
            case LinkOutcome.Conflict: return Results.Conflict();
        }

        var view = await Project(db, db.Links.AsNoTracking().Where(l => l.Id == id)).FirstOrDefaultAsync();
        return view is null ? Results.NotFound() : Results.Ok(view);
    }

    private static async Task<IResult> UndoAsync(Guid id, LinkService links, CancellationToken ct) =>
        await links.UndoAsync(id, ct) switch
        {
            LinkOutcome.NotFound => Results.NotFound(),
            LinkOutcome.Conflict => Results.Conflict(),
            _ => Results.NoContent(),
        };
}
