using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceDashboard.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure.Importing;

public sealed record ImportCheckpointView(DateOnly Date, decimal Amount);

public sealed record ImportLineView(DateOnly Date, decimal Amount, string Description, TransactionState State, bool Existing);

public sealed record ImportPreviewView(
    Guid BatchId,
    Guid AccountId,
    ImportSource Source,
    ImportBatchState State,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int NewCount,
    int ExistingCount,
    IReadOnlyList<ImportWarning> Warnings,
    ImportCheckpointView? Checkpoint,
    IReadOnlyList<ImportLineView> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ImportBatchSummary(
    Guid BatchId,
    Guid AccountId,
    ImportSource Source,
    ImportBatchState State,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int NewCount,
    int ExistingCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>One parsed line as kept in <see cref="ImportBatch.PreviewJson"/>: everything confirm needs, no re-upload.</summary>
public sealed record StoredPreviewLine(
    string FitId,
    DateOnly Date,
    decimal Amount,
    string Memo,
    TransactionState State,
    string? CounterpartyName,
    string? CounterpartyDocument,
    bool Existing);

/// <summary>Shape of <see cref="ImportBatch.PreviewJson"/> (jsonb). The original file is never stored.</summary>
public sealed record StoredPreview(
    string? AccountExternalId,
    ImportCheckpointView? Checkpoint,
    IReadOnlyList<StoredPreviewLine> Lines)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static StoredPreview? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<StoredPreview>(json, Options);

    internal static string SerializeWarnings(IReadOnlyList<ImportWarning> warnings) =>
        JsonSerializer.Serialize(warnings, Options);

    internal static IReadOnlyList<ImportWarning> DeserializeWarnings(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<ImportWarning>>(json, Options) ?? [];
}

public enum PreviewError { AccountNotFound, AccountArchived, AccountTypeMismatch, InvalidFile, TooLarge }

public sealed record PreviewOutcome(ImportPreviewView? Preview, PreviewError? Error, string? ErrorKind)
{
    public static PreviewOutcome Ok(ImportPreviewView preview) => new(preview, null, null);
    public static PreviewOutcome Fail(PreviewError error, string? kind = null) => new(null, error, kind);
}

/// <summary>Upload → preview. Writes only the <see cref="ImportBatch"/> (state Preview); never transactions or checkpoints.</summary>
public sealed class ImportService(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromHours(24);

    public async Task<PreviewOutcome> CreatePreviewAsync(Guid accountId, byte[] content, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return PreviewOutcome.Fail(PreviewError.AccountNotFound);
        if (account.IsArchived) return PreviewOutcome.Fail(PreviewError.AccountArchived);
        if (content.Length > NubankOfxReader.MaxBytes) return PreviewOutcome.Fail(PreviewError.TooLarge, "too_large");

        ParsedStatement parsed;
        try
        {
            parsed = NubankOfxReader.Parse(content);
        }
        catch (ImportFormatException e)
        {
            return e.Kind == "too_large"
                ? PreviewOutcome.Fail(PreviewError.TooLarge, e.Kind)
                : PreviewOutcome.Fail(PreviewError.InvalidFile, e.Kind);
        }

        var expectedType = parsed.Source == ImportSource.NubankCard ? AccountType.CreditCard : AccountType.Checking;
        if (account.Type != expectedType) return PreviewOutcome.Fail(PreviewError.AccountTypeMismatch, "account_type_mismatch");

        var now = time.GetUtcNow();
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        await DiscardExpiredAsync(now, ct);
        // Re-sending the same file replaces its open preview.
        await db.ImportBatches.Where(b => b.State == ImportBatchState.Preview && b.FileHash == hash).ExecuteDeleteAsync(ct);

        var fitIds = parsed.Lines.Select(l => l.FitId).Distinct().ToList();
        // Existing = same key in this account, soft-deleted included (no Visible()).
        var inAccount = (await db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == account.Id && t.SourceKey != null && fitIds.Contains(t.SourceKey))
                .Select(t => t.SourceKey!).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var inOtherAccounts = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId != account.Id && t.SourceKey != null && fitIds.Contains(t.SourceKey))
            .Select(t => t.SourceKey!).Distinct().CountAsync(ct);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<StoredPreviewLine>(parsed.Lines.Count);
        foreach (var l in parsed.Lines)
        {
            // A repeated FITID inside the file is imported once: later copies count as existing.
            var existing = inAccount.Contains(l.FitId) || !seen.Add(l.FitId);
            var (name, document) = NubankRules.ExtractCounterparty(l.Memo);
            lines.Add(new StoredPreviewLine(l.FitId, l.Date, l.Amount, l.Memo,
                NubankRules.Classify(parsed.Source, l.Memo), name, document, existing));
        }

        var existingCount = lines.Count(l => l.Existing);
        var newCount = lines.Count - existingCount;
        var warnings = new List<ImportWarning>();

        var alreadyImported = await db.ImportBatches
            .AnyAsync(b => b.State == ImportBatchState.Confirmed && b.FileHash == hash, ct);
        if (alreadyImported)
        {
            warnings.Add(ImportWarning.AlreadyImported());
            newCount = 0;
        }

        if (!string.IsNullOrWhiteSpace(account.ExternalIdentifier) && !string.IsNullOrWhiteSpace(parsed.AccountExternalId)
            && !string.Equals(account.ExternalIdentifier.Trim(), parsed.AccountExternalId.Trim(), StringComparison.Ordinal))
        {
            warnings.Add(ImportWarning.AccountIdentifierMismatch());
        }

        if (inOtherAccounts > 0) warnings.Add(ImportWarning.FitidInOtherAccount(inOtherAccounts));

        var outside = lines.Count(l => (parsed.PeriodStart is { } s && l.Date < s) || (parsed.PeriodEnd is { } e && l.Date > e));
        if (outside > 0) warnings.Add(ImportWarning.DateOutsidePeriod(outside));

        if (parsed.LedgerBalance is { } ledger && await BalanceMismatchAsync(account.Id, ledger, lines, ct))
        {
            warnings.Add(ImportWarning.BalanceMismatch());
        }

        if (parsed.PeriodStart is { } start && parsed.PeriodEnd is { } end)
        {
            var periodWarning = await PeriodWarningAsync(account.Id, hash, start, end, ct);
            if (periodWarning is not null) warnings.Add(periodWarning);
        }

        var checkpoint = parsed.LedgerBalance is { } lb ? new ImportCheckpointView(lb.AsOf, lb.Amount) : null;
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Source = parsed.Source,
            FileHash = hash,
            PeriodStart = parsed.PeriodStart,
            PeriodEnd = parsed.PeriodEnd,
            State = ImportBatchState.Preview,
            CreatedAt = now,
            ExpiresAt = now + PreviewLifetime,
            PreviewJson = new StoredPreview(parsed.AccountExternalId, checkpoint, lines).Serialize(),
            WarningsJson = StoredPreview.SerializeWarnings(warnings),
            NewCount = newCount,
            ExistingCount = existingCount,
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        return PreviewOutcome.Ok(ToView(batch, warnings, checkpoint, lines));
    }

    public async Task<ImportPreviewView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return null;
        var stored = StoredPreview.Deserialize(batch.PreviewJson);
        return ToView(batch, StoredPreview.DeserializeWarnings(batch.WarningsJson), stored?.Checkpoint, stored?.Lines ?? []);
    }

    public async Task<IReadOnlyList<ImportBatchSummary>> ListAsync(CancellationToken ct = default)
    {
        await DiscardExpiredAsync(time.GetUtcNow(), ct);
        return await db.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Select(b => new ImportBatchSummary(b.Id, b.AccountId, b.Source, b.State, b.PeriodStart, b.PeriodEnd,
                b.NewCount, b.ExistingCount, b.CreatedAt, b.ExpiresAt))
            .ToListAsync(ct);
    }

    /// <summary>Lazy expiry: drops the current user's previews past <see cref="ImportBatch.ExpiresAt"/> (query filter scopes it).</summary>
    private Task<int> DiscardExpiredAsync(DateTimeOffset now, CancellationToken ct) =>
        db.ImportBatches.Where(b => b.State == ImportBatchState.Preview && b.ExpiresAt <= now).ExecuteDeleteAsync(ct);

    /// <summary>LEDGERBAL vs. previous checkpoint + visible stored lines + new file lines up to the ledger date.</summary>
    private async Task<bool> BalanceMismatchAsync(
        Guid accountId, ParsedBalance ledger, IReadOnlyList<StoredPreviewLine> lines, CancellationToken ct)
    {
        var checkpoints = await db.Checkpoints.AsNoTracking()
            .Where(c => c.AccountId == accountId && c.Date <= ledger.AsOf).ToListAsync(ct);
        var previous = BalanceCalculator.Latest(checkpoints);
        if (previous is null) return false;

        var from = previous.Date;
        var stored = await db.Transactions.AsNoTracking().Visible()
            .Where(t => t.AccountId == accountId && t.Date > from && t.Date <= ledger.AsOf)
            .SumAsync(t => t.Amount, ct);
        var fromFile = lines.Where(l => !l.Existing && l.Date > from && l.Date <= ledger.AsOf).Sum(l => l.Amount);
        return previous.Amount + stored + fromFile != ledger.Amount;
    }

    /// <summary>Compares the file period with the account's confirmed batches (other files only).</summary>
    private async Task<ImportWarning?> PeriodWarningAsync(
        Guid accountId, string hash, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var periods = await db.ImportBatches.AsNoTracking()
            .Where(b => b.AccountId == accountId && b.State == ImportBatchState.Confirmed && b.FileHash != hash
                        && b.PeriodStart != null && b.PeriodEnd != null)
            .Select(b => new { Start = b.PeriodStart!.Value, End = b.PeriodEnd!.Value })
            .ToListAsync(ct);
        if (periods.Count == 0) return null;
        if (periods.Any(p => p.Start <= end && start <= p.End)) return ImportWarning.PeriodOverlap();

        var before = periods.Where(p => p.End < start).Select(p => (DateOnly?)p.End).Max();
        var after = periods.Where(p => p.Start > end).Select(p => (DateOnly?)p.Start).Min();
        var gap = (before is { } b && b.AddDays(1) < start) || (after is { } a && end.AddDays(1) < a);
        return gap ? ImportWarning.PeriodGap() : null;
    }

    private static ImportPreviewView ToView(
        ImportBatch batch, IReadOnlyList<ImportWarning> warnings, ImportCheckpointView? checkpoint,
        IReadOnlyList<StoredPreviewLine> lines) =>
        new(batch.Id, batch.AccountId, batch.Source, batch.State, batch.PeriodStart, batch.PeriodEnd,
            batch.NewCount, batch.ExistingCount, warnings, checkpoint,
            lines.Select(l => new ImportLineView(l.Date, l.Amount, l.Memo, l.State, l.Existing)).ToList(),
            batch.CreatedAt, batch.ExpiresAt);
}
