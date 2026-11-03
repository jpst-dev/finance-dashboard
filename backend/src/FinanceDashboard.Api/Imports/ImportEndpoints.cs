using FinanceDashboard.Api.Security;
using FinanceDashboard.Infrastructure.Importing;
using Microsoft.AspNetCore.Mvc;

namespace FinanceDashboard.Api.Imports;

public sealed record ImportErrorResponse(string Error);

public sealed record ConfirmImportRequest(bool AcknowledgeBlockingWarnings = false);

public sealed record BlockingWarningsResponse(string Error, IReadOnlyList<ImportWarning> Warnings);

public static class ImportEndpoints
{
    /// <summary>Whole multipart request: the 5 MB file plus a small envelope (boundaries, accountId).</summary>
    public const long MaxRequestBytes = NubankOfxReader.MaxBytes + 64 * 1024;

    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").RequireAuthorization().WithTags("Imports");

        group.MapGet("/", async (ImportService imports, CancellationToken ct) => Results.Ok(await imports.ListAsync(ct)))
            .WithName("ListImports").WithSummary("List the user's import batches (expired previews are discarded)")
            .Produces<List<ImportBatchSummary>>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (Guid id, ImportService imports, CancellationToken ct) =>
                await imports.GetAsync(id, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .WithName("GetImport").WithSummary("Get one import batch with its preview")
            .Produces<ImportPreviewView>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // Filters run in order: size first (before anything reads the form), then the explicit X-XSRF-TOKEN check.
        // DisableAntiforgery: the built-in form antiforgery must not validate a second time.
        group.MapPost("/", UploadAsync)
            .AddEndpointFilter(RejectOversizedAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            // Keep the upload in memory (never spooled to a temp file) and cap the multipart body.
            .WithFormOptions(
                memoryBufferThreshold: (int)MaxRequestBytes,
                bufferBodyLengthLimit: MaxRequestBytes,
                multipartBodyLengthLimit: MaxRequestBytes,
                valueCountLimit: 8)
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("UploadImport")
            .WithSummary("Upload a Nubank OFX (multipart: file, accountId) and get a preview; nothing is recorded yet")
            .Produces<ImportPreviewView>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces<ImportErrorResponse>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ImportErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/confirm", ConfirmAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("ConfirmImport")
            .WithSummary("Record a preview's new lines and checkpoint; blocking warnings need acknowledgeBlockingWarnings=true")
            .Produces<ConfirmResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<BlockingWarningsResponse>(StatusCodes.Status409Conflict)
            .Produces<ImportErrorResponse>(StatusCodes.Status410Gone)
            .Produces<ImportErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/undo", UndoAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("UndoImport")
            .WithSummary("Undo a confirmed batch (removes its lines, edited and hidden ones included, and its checkpoint) or drop a preview")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ImportErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, ConfirmImportRequest? request, ImportService imports, CancellationToken ct)
    {
        var outcome = await imports.ConfirmAsync(id, request?.AcknowledgeBlockingWarnings ?? false, ct);
        return outcome.Error switch
        {
            null => Results.Ok(outcome.Result),
            ConfirmError.NotFound => Results.NotFound(),
            ConfirmError.Expired => Error("preview_expired", StatusCodes.Status410Gone),
            ConfirmError.AlreadyConfirmed => Error("already_confirmed", StatusCodes.Status409Conflict),
            ConfirmError.AlreadyUndone => Error("already_undone", StatusCodes.Status409Conflict),
            ConfirmError.AlreadyImported => Error("already_imported", StatusCodes.Status409Conflict),
            ConfirmError.AccountArchived => Error("account_archived", StatusCodes.Status409Conflict),
            ConfirmError.BlockingWarnings => Results.Conflict(new BlockingWarningsResponse("blocking_warnings", outcome.Warnings)),
            ConfirmError.UnsupportedAmountPrecision => Error("unsupported_amount_precision", StatusCodes.Status422UnprocessableEntity),
            _ => Error("unsupported_line", StatusCodes.Status422UnprocessableEntity),
        };
    }

    private static async Task<IResult> UndoAsync(Guid id, ImportService imports, CancellationToken ct) =>
        await imports.UndoAsync(id, ct) switch
        {
            null => Results.NoContent(),
            UndoError.AlreadyUndone => Error("already_undone", StatusCodes.Status409Conflict),
            _ => Results.NotFound(),
        };

    private static IResult Error(string error, int status) => Results.Json(new ImportErrorResponse(error), statusCode: status);

    private static async ValueTask<object?> RejectOversizedAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next) =>
        ctx.HttpContext.Request.ContentLength > MaxRequestBytes ? TooLarge() : await next(ctx);

    private static async Task<IResult> UploadAsync(HttpContext http, ImportService imports, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType) return Results.BadRequest();

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (Exception e) when (e is BadHttpRequestException or InvalidDataException or IOException)
        {
            return Results.BadRequest();
        }

        if (form.Files.Count != 1 || form.Files[0].Name != "file") return Results.BadRequest();
        var ids = form["accountId"];
        if (ids.Count != 1 || !Guid.TryParse(ids[0], out var accountId)) return Results.BadRequest();

        var file = form.Files[0];
        if (file.Length > NubankOfxReader.MaxBytes) return TooLarge();
        var content = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(content, ct);
        }

        var outcome = await imports.CreatePreviewAsync(accountId, content, ct);
        return outcome.Error switch
        {
            null => Results.Ok(outcome.Preview),
            PreviewError.AccountNotFound => Results.NotFound(),
            PreviewError.AccountArchived => Results.Conflict(),
            PreviewError.TooLarge => TooLarge(),
            _ => Results.UnprocessableEntity(new ImportErrorResponse(outcome.ErrorKind ?? "invalid")),
        };
    }

    private static IResult TooLarge() =>
        Results.Json(new ImportErrorResponse("too_large"), statusCode: StatusCodes.Status413PayloadTooLarge);
}
