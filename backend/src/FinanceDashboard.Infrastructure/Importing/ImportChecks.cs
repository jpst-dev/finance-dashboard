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
    /// Invoice re-import: how many lines of the account in the invoice scope (<c>StatementDate == dueDate</c>, with a
    /// key, not soft-deleted — edited ones included) have no (key, ordinal) pair in the file. Soft-deleted lines do
    /// not count: they keep their key and never come back anyway.
    /// </summary>
    public async Task<int> MissingInvoiceLinesAsync(
        Guid accountId, DateOnly dueDate, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)
    {
        var inFile = lines.Select(l => (l.SourceKey, l.Ordinal)).ToHashSet();
        var stored = await db.Transactions.AsNoTracking().Visible()
            .Where(t => t.AccountId == accountId && t.StatementDate == dueDate && t.SourceKey != null)
            .Select(t => new { Key = t.SourceKey!, t.Ordinal })
            .ToListAsync(ct);
        return stored.Count(t => !inFile.Contains((t.Key, t.Ordinal)));
    }

    /// <summary>
    /// 0 when the account has no line with a card suffix yet (first invoice import: nothing to compare with);
    /// otherwise how many lines of the file carry a suffix the account has never seen (soft-deleted lines included).
    /// </summary>
    public async Task<int> UnknownCardSuffixLinesAsync(Guid accountId, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)
    {
        var known = (await db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == accountId && t.CardSuffix != null)
                .Select(t => t.CardSuffix!).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        if (known.Count == 0) return 0;
        return lines.Count(l => l.CardSuffix is { } suffix && !known.Contains(suffix));
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
