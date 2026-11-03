using System.Globalization;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Installments;

namespace FinanceDashboard.Api.Installments;

public sealed record InstallmentAccountResponse(Guid AccountId, string AccountName, DateOnly BasedOn);

public sealed record InstallmentItemResponse(
    Guid AccountId, string Description, string? CardSuffix, int InstallmentNumber, int InstallmentTotal, decimal Amount);

public sealed record InstallmentMonthResponse(string Month, decimal Total, IReadOnlyList<InstallmentItemResponse> Items);

public sealed record InstallmentGroupResponse(
    Guid AccountId, string AccountName, string? CardSuffix, string Description, int InstallmentNumber,
    int InstallmentTotal, int Remaining, decimal Amount, int Multiplicity, DateOnly EndsOn, decimal RemainingTotal);

public sealed record InstallmentsResponse(
    decimal TotalCommitted, IReadOnlyList<InstallmentAccountResponse> Accounts,
    IReadOnlyList<InstallmentMonthResponse> Months, IReadOnlyList<InstallmentGroupResponse> Groups);

public static class InstallmentEndpoints
{
    public static IEndpointRouteBuilder MapInstallments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/installments").RequireAuthorization().WithTags("Installments");

        group.MapGet("/", GetAsync)
            .WithName("GetInstallments").WithSummary("Committed future card installments projected from the latest statement")
            .Produces<InstallmentsResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetAsync(InstallmentService service, CancellationToken ct)
    {
        var p = await service.GetAsync(ct);
        return Results.Ok(new InstallmentsResponse(
            p.TotalCommitted,
            p.Accounts.Select(a => new InstallmentAccountResponse(a.AccountId, a.AccountName, a.BasedOn)).ToList(),
            p.Months.Select(m => new InstallmentMonthResponse(m.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture), m.Total,
                m.Items.Select(i => new InstallmentItemResponse(i.AccountId, i.Description, i.CardSuffix,
                    i.InstallmentNumber, i.InstallmentTotal, i.Amount)).ToList())).ToList(),
            p.Groups.Select(g => new InstallmentGroupResponse(g.AccountId, g.AccountName, g.CardSuffix, g.Description,
                g.InstallmentNumber, g.InstallmentTotal, g.Remaining, g.Amount, g.Multiplicity, g.EndsOn,
                g.RemainingTotal)).ToList()));
    }
}
