using FinanceDashboard.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceDashboard.Infrastructure.Categorization;

/// <summary>A rule as listed: <see cref="Matches"/> = candidate lines it would catch now, ignoring precedence.</summary>
public sealed record RuleListItem(
    Guid Id, string Pattern, Guid CategoryId, string CategoryName, DateTimeOffset CreatedAt, int Matches);

public enum CreateRuleOutcome { Created, InvalidPattern, CategoryNotFound, Duplicate }

public sealed record CreateRuleResult(CreateRuleOutcome Outcome, RuleListItem? Rule);

/// <summary>
/// The user's category rules (spec S3(c)). Rules only fill lines that have no category: imported, visible, Normal,
/// not edited by the user and not neutral (no Confirmed link). A rule's category never overwrites a category written
/// by anyone else, and is not an edit (<see cref="Transaction.EditedByUser"/> stays false).
/// </summary>
public sealed class CategoryRuleService(AppDbContext db, TimeProvider time)
{
    /// <summary>Raw input limit before normalization (the stored pattern is at most <see cref="CategoryRuleMatcher.MaxPatternLength"/>).</summary>
    public const int MaxRawPattern = 200;

    /// <summary>
    /// Gives the winning rule's category to every candidate line (only those of <paramref name="batchId"/> when given);
    /// returns how many lines changed. Runs under the per-user lock of the reconciliation, joining the ambient
    /// transaction (import confirm) when there is one.
    /// </summary>
    public Task<int> ApplyAsync(Guid? batchId = null, CancellationToken ct = default) =>
        UserLock.InLockedTransactionAsync(db, () => ApplyCoreAsync(batchId, ct), ct);

    public async Task<IReadOnlyList<RuleListItem>> ListAsync(CancellationToken ct = default)
    {
        var rules = await (
                from r in db.CategoryRules.AsNoTracking()
                join c in db.Categories.AsNoTracking() on r.CategoryId equals c.Id
                select new { r.Id, r.Pattern, r.CategoryId, c.Name, c.Kind, r.CreatedAt })
            .ToListAsync(ct);
        if (rules.Count == 0) return [];

        var lines = await NormalizedCandidatesAsync(ct);
        return rules
            .OrderBy(r => r.Pattern, StringComparer.Ordinal)
            .Select(r => new RuleListItem(r.Id, r.Pattern, r.CategoryId, r.Name, r.CreatedAt, Count(lines, r.Pattern, r.Kind)))
            .ToList();
    }

    /// <summary>Creates a rule (it is not applied: the user asks for that). The pattern is stored normalized.</summary>
    public async Task<CreateRuleResult> CreateAsync(string pattern, Guid categoryId, CancellationToken ct = default)
    {
        if (pattern is null || pattern.Length > MaxRawPattern
            || CategoryRuleMatcher.NormalizePattern(pattern) is not { } normalized)
        {
            return new CreateRuleResult(CreateRuleOutcome.InvalidPattern, null);
        }

        // Query filter: another user's category is not found, exactly like a missing one.
        var category = await db.Categories.AsNoTracking().Where(c => c.Id == categoryId)
            .Select(c => new { c.Name, c.Kind }).FirstOrDefaultAsync(ct);
        if (category is null) return new CreateRuleResult(CreateRuleOutcome.CategoryNotFound, null);
        if (await db.CategoryRules.AnyAsync(r => r.Pattern == normalized, ct))
            return new CreateRuleResult(CreateRuleOutcome.Duplicate, null);

        var rule = new CategoryRule
        {
            Id = Guid.NewGuid(), Pattern = normalized, CategoryId = categoryId, CreatedAt = time.GetUtcNow(),
        };
        db.CategoryRules.Add(rule);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException pg
            && pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // A simultaneous creation of the same pattern, or the category deleted since it was read.
            db.Entry(rule).State = EntityState.Detached;
            return new CreateRuleResult(
                pg.SqlState == PostgresErrorCodes.UniqueViolation ? CreateRuleOutcome.Duplicate : CreateRuleOutcome.CategoryNotFound,
                null);
        }

        var matches = Count(await NormalizedCandidatesAsync(ct), normalized, category.Kind);
        return new CreateRuleResult(CreateRuleOutcome.Created,
            new RuleListItem(rule.Id, rule.Pattern, rule.CategoryId, category.Name, rule.CreatedAt, matches));
    }

    /// <summary>Deletes the rule; the categories it already gave stay (reverting is manual).</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await db.CategoryRules.Where(r => r.Id == id).ExecuteDeleteAsync(ct) > 0;

    private async Task<int> ApplyCoreAsync(Guid? batchId, CancellationToken ct)
    {
        var rules = await db.CategoryRules.AsNoTracking()
            .Select(r => new { r.Id, r.Pattern, r.CategoryId, r.CreatedAt }).ToListAsync(ct);
        if (rules.Count == 0) return 0;

        var lines = Candidates();
        if (batchId is { } batch) lines = lines.Where(t => t.BatchId == batch);
        var candidates = await lines.Select(t => new { t.Id, t.OriginalDescription, t.Amount }).ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        // Held until commit, so a concurrent delete of a category waits and the update's foreign key holds. A category
        // deleted before the lock is simply missing: its rules (gone with it, by cascade) are skipped.
        var kinds = await LockCategoriesAsync(rules.Select(r => r.CategoryId).Distinct().ToArray(), ct);
        var views = rules.Where(r => kinds.ContainsKey(r.CategoryId))
            .Select(r => new RuleView(r.Id, r.Pattern, r.CategoryId, kinds[r.CategoryId], r.CreatedAt))
            .ToList();
        if (views.Count == 0) return 0;
        var categoryOf = views.ToDictionary(v => v.Id, v => v.CategoryId);

        // Winner normalizes each description once and checks the sign against the category kind.
        var byCategory = candidates
            .Select(c => (c.Id, Rule: CategoryRuleMatcher.Winner(c.OriginalDescription, c.Amount, views)))
            .Where(x => x.Rule is not null)
            .GroupBy(x => categoryOf[x.Rule!.Value], x => x.Id);

        var applied = 0;
        foreach (var group in byCategory)
        {
            var claimed = await ClaimAsync(group.ToArray(), ct);
            if (claimed.Length == 0) continue;

            // The guard is repeated here: a category written by anyone else is never overwritten.
            var guarded = db.Transactions.Where(t => claimed.Contains(t.Id)
                && t.CategoryId == null && t.State == TransactionState.Normal && !t.IsDeleted && !t.EditedByUser);
            guarded = kinds[group.Key] == CategoryKind.Expense
                ? guarded.Where(t => t.Amount < 0)
                : guarded.Where(t => t.Amount > 0);
            var category = group.Key;
            applied += await guarded.ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)category), ct);
        }

        return applied;
    }

    /// <summary>
    /// Candidate lines: imported, visible, Normal, without a category, not edited by the user and not neutral
    /// (no Confirmed link on either side). Manual and edited lines left without a category are the user's decision.
    /// </summary>
    private IQueryable<Transaction> Candidates() =>
        db.Transactions.AsNoTracking().Visible().WithoutConfirmedLink(db)
            .Where(t => t.BatchId != null && t.State == TransactionState.Normal && t.CategoryId == null
                && !t.EditedByUser && t.Amount != 0);

    /// <summary>Each candidate normalized once, with the category kind its sign accepts.</summary>
    private async Task<List<(string Text, CategoryKind Kind)>> NormalizedCandidatesAsync(CancellationToken ct)
    {
        var lines = await Candidates().Select(t => new { t.OriginalDescription, t.Amount }).ToListAsync(ct);
        var result = new List<(string, CategoryKind)>(lines.Count);
        foreach (var l in lines)
        {
            if (NameKeys.Normalize(l.OriginalDescription) is { } text)
                result.Add((text, l.Amount < 0 ? CategoryKind.Expense : CategoryKind.Income));
        }

        return result;
    }

    private static int Count(List<(string Text, CategoryKind Kind)> lines, string pattern, CategoryKind kind) =>
        lines.Count(l => l.Kind == kind && CategoryRuleMatcher.Matches(l.Text, pattern));

    private sealed class LockedCategory
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "";
    }

    /// <summary>Locks (FOR KEY SHARE, as the import does for overrides) the user's categories among <paramref name="ids"/>; returns their kinds.</summary>
    private async Task<Dictionary<Guid, CategoryKind>> LockCategoriesAsync(Guid[] ids, CancellationToken ct)
    {
        var userId = db.CurrentUserId ?? throw new InvalidOperationException("No authenticated user.");
        var found = await db.Database.SqlQueryRaw<LockedCategory>(
                "SELECT \"Id\", \"Kind\" FROM \"Categories\" WHERE \"UserId\" = @user AND \"Id\" = ANY(@ids) FOR KEY SHARE",
                new NpgsqlParameter("user", userId), new NpgsqlParameter("ids", ids))
            .ToListAsync(ct);
        return found.ToDictionary(c => c.Id, c => Enum.Parse<CategoryKind>(c.Kind));
    }

    /// <summary>
    /// Row-locks the lines still uncategorized among <paramref name="ids"/>, skipping the ones another transaction
    /// holds. A line being edited right now is skipped instead of waited on: the edit marks it as the user's (never a
    /// candidate again), and waiting could deadlock with an edit that is itself waiting for the per-user lock.
    /// </summary>
    private async Task<Guid[]> ClaimAsync(Guid[] ids, CancellationToken ct)
    {
        var userId = db.CurrentUserId ?? throw new InvalidOperationException("No authenticated user.");
        var claimed = await db.Database.SqlQueryRaw<Guid>(
                """
                SELECT "Id" AS "Value" FROM "Transactions"
                WHERE "UserId" = @user AND "Id" = ANY(@ids) AND "CategoryId" IS NULL
                FOR NO KEY UPDATE SKIP LOCKED
                """,
                new NpgsqlParameter("user", userId), new NpgsqlParameter("ids", ids))
            .ToListAsync(ct);
        return claimed.ToArray();
    }
}
