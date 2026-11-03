using FinanceDashboard.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceDashboard.Infrastructure;

public static class DefaultCategories
{
    private static readonly string[] Expenses =
        ["Alimentação", "Moradia", "Transporte", "Saúde", "Lazer", "Educação", "Assinaturas", "Compras", "Impostos e taxas", "Outros"];

    private static readonly string[] Incomes = ["Salário", "Rendimentos", "Outras receitas"];

    /// <summary>Adds the default categories a user is missing (matched by kind + name). Idempotent.</summary>
    public static async Task EnsureForUserAsync(AppDbContext db, string userId, CancellationToken ct = default)
    {
        var existing = await db.Categories.IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Kind, c.Name })
            .ToListAsync(ct);
        var have = existing.Select(c => (c.Kind, c.Name)).ToHashSet();

        var added = false;
        foreach (var (kind, names) in new[] { (CategoryKind.Expense, Expenses), (CategoryKind.Income, Incomes) })
        {
            foreach (var name in names.Where(n => !have.Contains((kind, n))))
            {
                db.Categories.Add(new Category { Id = Guid.NewGuid(), UserId = userId, Name = name, Kind = kind, IsDefault = true });
                added = true;
            }
        }

        if (added) await db.SaveChangesAsync(ct);
    }

    public static async Task EnsureForAllUsersAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userIds = await db.Users.Select(u => u.Id).ToListAsync(ct);
        foreach (var id in userIds) await EnsureForUserAsync(db, id, ct);
    }
}
