using FinanceDashboard.Api.Security;
using FinanceDashboard.Infrastructure.Categorization;

namespace FinanceDashboard.Api.CategoryRules;

/// <summary>A rule as the API shows it. <c>Matches</c> = lines it would catch now, ignoring precedence.</summary>
public sealed record CategoryRuleResponse(
    Guid Id, string Pattern, Guid CategoryId, string CategoryName, DateTimeOffset CreatedAt, int Matches);

/// <summary>Body of POST /api/category-rules.</summary>
public sealed record CreateCategoryRuleRequest(string? Pattern, Guid CategoryId);

/// <summary>Body of a 400/409 on rule creation.</summary>
public sealed record CategoryRuleErrorResponse(string Error);

public sealed record ApplyRulesResponse(int Applied);

public static class CategoryRuleEndpoints
{
    public static IEndpointRouteBuilder MapCategoryRules(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/category-rules").RequireAuthorization().WithTags("CategoryRules");

        group.MapGet("/", ListAsync)
            .WithName("ListCategoryRules").WithSummary("List the category rules with the lines each would catch now")
            .Produces<CategoryRuleResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("CreateCategoryRule").WithSummary("Create a rule (it is not applied until asked)")
            .Produces<CategoryRuleResponse>(StatusCodes.Status201Created)
            .Produces<CategoryRuleErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<CategoryRuleErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/apply", ApplyAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("ApplyCategoryRules").WithSummary("Give uncategorized lines the category of their winning rule")
            .Produces<ApplyRulesResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("DeleteCategoryRule").WithSummary("Delete a rule (categories it already gave stay)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static CategoryRuleResponse ToResponse(RuleListItem r) =>
        new(r.Id, r.Pattern, r.CategoryId, r.CategoryName, r.CreatedAt, r.Matches);

    private static async Task<IResult> ListAsync(CategoryRuleService rules, CancellationToken ct) =>
        Results.Ok((await rules.ListAsync(ct)).Select(ToResponse).ToArray());

    // Patterns are the user's text (can hold merchant or counterparty names): never logged or echoed back.
    private static async Task<IResult> CreateAsync(CreateCategoryRuleRequest request, CategoryRuleService rules, CancellationToken ct)
    {
        var result = await rules.CreateAsync(request.Pattern, request.CategoryId, ct);
        return result.Outcome switch
        {
            CreateRuleOutcome.InvalidPattern => Results.BadRequest(new CategoryRuleErrorResponse("invalid_pattern")),
            CreateRuleOutcome.CategoryNotFound => Results.NotFound(),
            CreateRuleOutcome.Duplicate => Results.Conflict(new CategoryRuleErrorResponse("duplicate_rule")),
            _ => Results.Created($"/api/category-rules/{result.Rule!.Id}", ToResponse(result.Rule)),
        };
    }

    private static async Task<IResult> ApplyAsync(CategoryRuleService rules, CancellationToken ct) =>
        Results.Ok(new ApplyRulesResponse(await rules.ApplyAsync(null, ct)));

    private static async Task<IResult> DeleteAsync(Guid id, CategoryRuleService rules, CancellationToken ct) =>
        await rules.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
}
