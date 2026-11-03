using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure.Importing;

/// <summary>Preview/confirm checks that need the database. Scoped to the current user by the query filters.</summary>
internal sealed class ImportChecks(AppDbContext db)
{
    private const int DescriptionPrefix = 12;

    /// <summary>Which (key, ordinal) pairs already exist in the account, soft-deleted lines included (no Visible()).</summary>
    public async Task<HashSet<(string Key, int Ordinal)>> ExistingPairsAsync(
        Guid accountId, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return [];
        var keyList = keys.Distinct(StringComparer.Ordinal).ToList();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.SourceKey != null && keyList.Contains(t.SourceKey))
            .Select(t => new { Key = t.SourceKey!, t.Ordinal })
            .ToListAsync(ct);
        return rows.Select(r => (r.Key, r.Ordinal)).ToHashSet();
    }

    /// <summary>How many distinct keys of the file already exist in another account of the user.</summary>
    public Task<int> KeysInOtherAccountsAsync(Guid accountId, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return Task.FromResult(0);
        var keyList = keys.Distinct(StringComparer.Ordinal).ToList();
        return db.Transactions.AsNoTracking()
            .Where(t => t.AccountId != accountId && t.SourceKey != null && keyList.Contains(t.SourceKey))
            .Select(t => t.SourceKey!).Distinct().CountAsync(ct);
    }

    /// <summary>
    /// New lines of the file for which the account has a visible line (imported or manual) whose key is null or none
    /// of the file's keys, with the same amount, a date at most one day apart and the same first 12 normalized
    /// characters of description. Sibling ordinals of one key are never compared with each other.
    /// </summary>
    public async Task<int> PossibleDuplicatesAsync(Guid accountId, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)
    {
        var fresh = lines.Where(l => !l.Existing).ToList();
        if (fresh.Count == 0) return 0;

        var keys = lines.Select(l => l.SourceKey).Distinct(StringComparer.Ordinal).ToList();
        var amounts = fresh.Select(l => l.Amount).Distinct().ToList();
        var from = fresh.Min(l => l.Date).AddDays(-1);
        var to = fresh.Max(l => l.Date).AddDays(1);
        var candidates = await db.Transactions.AsNoTracking().Visible()
            .Where(t => t.AccountId == accountId && t.Date >= from && t.Date <= to && amounts.Contains(t.Amount)
                        && (t.SourceKey == null || !keys.Contains(t.SourceKey)))
            .Select(t => new { t.Date, t.Amount, t.OriginalDescription })
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        var byAmount = candidates
            .Select(c => (c.Date, c.Amount, Prefix: Prefix(c.OriginalDescription)))
            .ToLookup(c => c.Amount);
        return fresh.Count(l =>
        {
            var prefix = Prefix(l.Memo);
            return byAmount[l.Amount].Any(c =>
                Math.Abs(c.Date.DayNumber - l.Date.DayNumber) <= 1 && string.Equals(c.Prefix, prefix, StringComparison.Ordinal));
        });
    }

    private static string Prefix(string description)
    {
        var normalized = SourceKeys.NormalizeText(description);
        return normalized.Length <= DescriptionPrefix ? normalized : normalized[..DescriptionPrefix];
    }
}
