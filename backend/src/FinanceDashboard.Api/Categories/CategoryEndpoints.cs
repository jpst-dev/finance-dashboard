using FinanceDashboard.Api.Security;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Categories;

public sealed record CategoryResponse(Guid Id, string Name, CategoryKind Kind, bool IsDefault);

public sealed record CreateCategoryRequest(string? Name, CategoryKind? Kind);

public sealed record UpdateCategoryRequest(string? Name);

public static class CategoryEndpoints
{
    private const int MaxName = 80;

    public static IEndpointRouteBuilder MapCategories(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Categories");

        group.MapGet("/", async (AppDbContext db) =>
                Results.Ok(await db.Categories.AsNoTracking()
                    .OrderBy(c => c.Kind).ThenBy(c => c.Name)
                    .Select(c => new CategoryResponse(c.Id, c.Name, c.Kind, c.IsDefault))
                    .ToListAsync()))
            .WithName("ListCategories").WithSummary("List the user's categories")
            .Produces<List<CategoryResponse>>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", async (CreateCategoryRequest request, AppDbContext db) =>
            {
                var name = request.Name?.Trim() ?? "";
                if (name.Length is < 1 or > MaxName || request.Kind is null) return Results.BadRequest();

                var category = new Category { Id = Guid.NewGuid(), Name = name, Kind = request.Kind.Value };
                db.Categories.Add(category);
                await db.SaveChangesAsync();
                return Results.Created($"/api/categories/{category.Id}", ToResponse(category));
            })
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("CreateCategory").WithSummary("Create a category")
            .Produces<CategoryResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest request, AppDbContext db) =>
            {
                var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
                if (category is null) return Results.NotFound();

                var name = request.Name?.Trim() ?? "";
                if (name.Length is < 1 or > MaxName) return Results.BadRequest();

                category.Name = name;
                await db.SaveChangesAsync();
                return Results.Ok(ToResponse(category));
            })
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("RenameCategory").WithSummary("Rename a category")
            .Produces<CategoryResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
            {
                var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
                if (category is null) return Results.NotFound();

                // Transactions keep existing: the FK sets their category to null.
                db.Categories.Remove(category);
                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .AddEndpointFilter(AntiforgeryFilter.ValidateAsync)
            .WithName("DeleteCategory").WithSummary("Delete a category (its transactions become uncategorized)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static CategoryResponse ToResponse(Category c) => new(c.Id, c.Name, c.Kind, c.IsDefault);
}
