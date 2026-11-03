using FinanceDashboard.Api.Security;
using FinanceDashboard.Infrastructure.Importing;
using Microsoft.AspNetCore.Mvc;

namespace FinanceDashboard.Api.Imports;

public sealed record ImportErrorResponse(string Error);

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

        return app;
    }

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
