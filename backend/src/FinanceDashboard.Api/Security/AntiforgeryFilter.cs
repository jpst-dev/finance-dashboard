using Microsoft.AspNetCore.Antiforgery;

namespace FinanceDashboard.Api.Security;

/// <summary>Endpoint filter: rejects the request with 400 unless the antiforgery token is valid.</summary>
public static class AntiforgeryFilter
{
    public static async ValueTask<object?> ValidateAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var antiforgery = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(ctx.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        return await next(ctx);
    }
}
