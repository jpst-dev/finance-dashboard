using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FinanceDashboard.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinanceDashboard.Infrastructure.Importing;

public sealed record ImportCheckpointView(DateOnly Date, decimal Amount);

/// <summary>A preview line. <see cref="SourceKey"/> (the FITID) is what a confirm override refers to.</summary>
public sealed record ImportLineView(string SourceKey, DateOnly Date, decimal Amount, string Description, TransactionState State, bool Existing);

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
    DateTimeOffset? ExpiresAt,
    int EditedCount);

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

/// <summary>
/// Classification chosen in the preview for one line, by its <see cref="SourceKey"/>. <see cref="State"/> and
/// <see cref="CategoryId"/> are independent; a null one keeps what the import rules gave the line.
/// </summary>
public sealed record ImportLineOverride(string SourceKey, TransactionState? State = null, Guid? CategoryId = null);

public sealed record ConfirmResult(Guid BatchId, int Created, int Skipped);

public enum ConfirmError
{
    NotFound,
    Expired,
    AlreadyConfirmed,
    AlreadyUndone,
    AlreadyImported,
    AccountArchived,
    BlockingWarnings,
    UnsupportedAmountPrecision,
    UnsupportedLine,
    InvalidOverride,
}

public sealed record ConfirmOutcome(ConfirmResult? Result, ConfirmError? Error, IReadOnlyList<ImportWarning> Warnings)
{
    public static ConfirmOutcome Ok(ConfirmResult result) => new(result, null, []);
    public static ConfirmOutcome Fail(ConfirmError error, IReadOnlyList<ImportWarning>? warnings = null) =>
        new(null, error, warnings ?? []);
}

public enum UndoError { NotFound, AlreadyUndone }

/// <summary>
/// Upload → preview (writes only the <see cref="ImportBatch"/> in state Preview), confirm (lines + checkpoint in one
/// database transaction) and undo (removes what the batch created and frees its file hash).
/// </summary>
public sealed partial class ImportService(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromHours(24);

    private const int MaxDescription = 500;     // Transactions.OriginalDescription / NormalizedDescription
    private const int MaxSourceKey = 200;       // Transactions.SourceKey
    private const int MaxCounterpartyName = 200;
    private const int MaxCounterpartyDocument = 64;
    private const decimal MaxAbsAmount = 10_000_000_000_000_000m; // numeric(18,2)
    private const string HashIndex = "IX_ImportBatches_UserId_FileHash";

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
        // Lets the client warn before an undo, which also removes the lines the user edited.
        var edited = await db.Transactions.CountAsync(t => t.BatchId == batch.Id && t.EditedByUser, ct);
        return ToView(batch, StoredPreview.DeserializeWarnings(batch.WarningsJson), stored?.Checkpoint, stored?.Lines ?? [],
            edited);
    }

    /// <summary>
    /// Records the stored preview of a batch. Re-validates against the current state; inserts only lines whose
    /// (account, FITID, 0) key is free — existing lines (edited or soft-deleted included) are never touched.
    /// Races are settled by the database: the conditional state change serializes confirms of the same batch,
    /// the partial unique hash index rejects a second confirm of the same file, and
    /// <c>ON CONFLICT DO NOTHING</c> on the (account, key, ordinal) index skips lines another confirm just wrote.
    /// </summary>
    /// <remarks>
    /// <paramref name="overrides"/> are validated before anything is written (unknown or repeated source key,
    /// category missing or of another user: <see cref="ConfirmError.InvalidOverride"/>). They reach only the rows
    /// this confirm actually inserts: a line that is existing (at preview time, or because another confirm recorded
    /// it since) is skipped by the insert, so its override is silently ignored and the stored row stays untouched.
    /// </remarks>
    public async Task<ConfirmOutcome> ConfirmAsync(
        Guid batchId, bool acknowledgeBlockingWarnings, IReadOnlyList<ImportLineOverride>? overrides = null,
        CancellationToken ct = default)
    {
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return ConfirmOutcome.Fail(ConfirmError.NotFound);
        if (batch.State != ImportBatchState.Preview) return ConfirmOutcome.Fail(StateError(batch.State));
        if (batch.ExpiresAt is { } expires && expires <= time.GetUtcNow()) return ConfirmOutcome.Fail(ConfirmError.Expired);

        var stored = StoredPreview.Deserialize(batch.PreviewJson);
        if (stored is null) return ConfirmOutcome.Fail(ConfirmError.NotFound);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == batch.AccountId, ct);
        if (account is null) return ConfirmOutcome.Fail(ConfirmError.NotFound);
        if (account.IsArchived) return ConfirmOutcome.Fail(ConfirmError.AccountArchived);

        // Values the columns cannot hold exactly refuse the whole file: never rounded or cut silently.
        var amounts = stored.Lines.Select(l => l.Amount).ToList();
        if (stored.Checkpoint is not null) amounts.Add(stored.Checkpoint.Amount);
        if (amounts.Any(a => a != decimal.Round(a, 2))) return ConfirmOutcome.Fail(ConfirmError.UnsupportedAmountPrecision);
        if (amounts.Any(a => Math.Abs(a) >= MaxAbsAmount) || stored.Lines.Any(l => l.FitId.Length > MaxSourceKey))
        {
            return ConfirmOutcome.Fail(ConfirmError.UnsupportedLine);
        }

        var byKey = ValidOverrides(stored, overrides);
        if (byKey is null) return ConfirmOutcome.Fail(ConfirmError.InvalidOverride);
        var categoryIds = byKey.Values.Where(o => o.CategoryId is not null).Select(o => o.CategoryId!.Value).Distinct().ToList();
        // Query filter: another user's category is not found, exactly like a missing one.
        if (categoryIds.Count > 0
            && await db.Categories.CountAsync(cat => categoryIds.Contains(cat.Id), ct) != categoryIds.Count)
        {
            return ConfirmOutcome.Fail(ConfirmError.InvalidOverride);
        }

        // Another batch of the same file (this one may have been confirmed since it was read: decided by the claim below).
        if (await db.ImportBatches.AnyAsync(
                b => b.Id != batch.Id && b.State == ImportBatchState.Confirmed && b.FileHash == batch.FileHash, ct))
        {
            return ConfirmOutcome.Fail(ConfirmError.AlreadyImported);
        }

        var blocking = await BlockingWarningsAsync(account, stored, ct);
        if (blocking.Count > 0 && !acknowledgeBlockingWarnings) return ConfirmOutcome.Fail(ConfirmError.BlockingWarnings, blocking);

        // One row per FITID; sorted so concurrent confirms take the key locks in the same order (no deadlock).
        var lines = stored.Lines.DistinctBy(l => l.FitId, StringComparer.Ordinal)
            .OrderBy(l => l.FitId, StringComparer.Ordinal).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Holds the chosen categories until commit, so a concurrent delete cannot break the insert's foreign key.
        if (categoryIds.Count > 0 && await LockCategoriesAsync(batch.UserId, categoryIds, ct) != categoryIds.Count)
        {
            await tx.RollbackAsync(ct);
            return ConfirmOutcome.Fail(ConfirmError.InvalidOverride);
        }

        int claimed;
        try
        {
            claimed = await db.ImportBatches
                .Where(b => b.Id == batch.Id && b.State == ImportBatchState.Preview)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.State, ImportBatchState.Confirmed)
                    .SetProperty(b => b.PreviewJson, (string?)null)
                    .SetProperty(b => b.ExpiresAt, (DateTimeOffset?)null), ct);
        }
        catch (Exception e) when (IsUniqueViolation(e, HashIndex))
        {
            return ConfirmOutcome.Fail(ConfirmError.AlreadyImported); // another batch of the same file won the race
        }

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            var state = await db.ImportBatches.AsNoTracking()
                .Where(b => b.Id == batch.Id).Select(b => (ImportBatchState?)b.State).FirstOrDefaultAsync(ct);
            return ConfirmOutcome.Fail(state is { } s2 ? StateError(s2) : ConfirmError.NotFound);
        }

        var created = await InsertLinesAsync(batch, lines, byKey, ct);
        if (stored.Checkpoint is { } checkpoint) await AddCheckpointAsync(batch, checkpoint, ct);

        var skipped = stored.Lines.Count - created;
        await db.ImportBatches.Where(b => b.Id == batch.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.NewCount, created).SetProperty(b => b.ExistingCount, skipped), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ConfirmOutcome.Ok(new ConfirmResult(batch.Id, created, skipped));
    }

    /// <summary>
    /// Confirmed batch: removes for real every line it created (hidden and user-edited ones included) and its
    /// checkpoint, and marks it Undone, which frees the file hash. Preview batch: removes the batch.
    /// </summary>
    public async Task<UndoError?> UndoAsync(Guid batchId, CancellationToken ct = default)
    {
        var state = await db.ImportBatches.AsNoTracking()
            .Where(b => b.Id == batchId).Select(b => (ImportBatchState?)b.State).FirstOrDefaultAsync(ct);
        if (state is null) return UndoError.NotFound;
        if (state == ImportBatchState.Undone) return UndoError.AlreadyUndone;

        if (state == ImportBatchState.Preview)
        {
            var removed = await db.ImportBatches
                .Where(b => b.Id == batchId && b.State == ImportBatchState.Preview).ExecuteDeleteAsync(ct);
            if (removed == 1) return null;
            // Confirmed in the meantime: undo that instead.
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.ImportBatches
            .Where(b => b.Id == batchId && b.State == ImportBatchState.Confirmed)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.State, ImportBatchState.Undone), ct);
        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            var now = await db.ImportBatches.AsNoTracking().AnyAsync(b => b.Id == batchId, ct);
            return now ? UndoError.AlreadyUndone : UndoError.NotFound;
        }

        // No Visible(): soft-deleted lines of the batch go too.
        await db.Transactions.Where(t => t.BatchId == batchId).ExecuteDeleteAsync(ct);
        await db.Checkpoints.Where(c => c.BatchId == batchId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }

    private static ConfirmError StateError(ImportBatchState state) => state switch
    {
        ImportBatchState.Confirmed => ConfirmError.AlreadyConfirmed,
        ImportBatchState.Undone => ConfirmError.AlreadyUndone,
        _ => ConfirmError.NotFound,
    };

    /// <summary>Overrides keyed by source key, or null when one is invalid (missing, repeated or not in the preview).</summary>
    private static Dictionary<string, ImportLineOverride>? ValidOverrides(
        StoredPreview stored, IReadOnlyList<ImportLineOverride>? overrides)
    {
        var byKey = new Dictionary<string, ImportLineOverride>(StringComparer.Ordinal);
        if (overrides is null) return byKey;

        var keys = stored.Lines.Select(l => l.FitId).ToHashSet(StringComparer.Ordinal);
        foreach (var o in overrides)
        {
            if (o?.SourceKey is null || !keys.Contains(o.SourceKey) || !byKey.TryAdd(o.SourceKey, o)) return null;
            if (o.State is { } state && !Enum.IsDefined(state)) return null;
        }

        return byKey;
    }

    /// <summary>Locks (FOR KEY SHARE) the user's categories among <paramref name="ids"/>; returns how many exist.</summary>
    private async Task<int> LockCategoriesAsync(string userId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var found = await db.Database.SqlQueryRaw<Guid>(
                "SELECT \"Id\" AS \"Value\" FROM \"Categories\" WHERE \"UserId\" = @user AND \"Id\" = ANY(@ids) FOR KEY SHARE",
                new NpgsqlParameter("user", userId), new NpgsqlParameter("ids", ids.ToArray()))
            .ToListAsync(ct);
        return found.Count;
    }

    /// <summary>The blocking checks of the preview, recomputed against the current state.</summary>
    private async Task<List<ImportWarning>> BlockingWarningsAsync(Account account, StoredPreview stored, CancellationToken ct)
    {
        var warnings = new List<ImportWarning>();
        if (!string.IsNullOrWhiteSpace(account.ExternalIdentifier) && !string.IsNullOrWhiteSpace(stored.AccountExternalId)
            && !string.Equals(account.ExternalIdentifier.Trim(), stored.AccountExternalId.Trim(), StringComparison.Ordinal))
        {
            warnings.Add(ImportWarning.AccountIdentifierMismatch());
        }

        var fitIds = stored.Lines.Select(l => l.FitId).Distinct().ToList();
        var inOtherAccounts = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId != account.Id && t.SourceKey != null && fitIds.Contains(t.SourceKey))
            .Select(t => t.SourceKey!).Distinct().CountAsync(ct);
        if (inOtherAccounts > 0) warnings.Add(ImportWarning.FitidInOtherAccount(inOtherAccounts));
        return warnings;
    }

    /// <summary>One INSERT … ON CONFLICT DO NOTHING over arrays; returns how many lines were actually created.</summary>
    /// <remarks>Overrides ride along per line; rows skipped by ON CONFLICT (existing lines) never get theirs.</remarks>
    private Task<int> InsertLinesAsync(
        ImportBatch batch, IReadOnlyList<StoredPreviewLine> lines, IReadOnlyDictionary<string, ImportLineOverride> overrides,
        CancellationToken ct)
    {
        if (lines.Count == 0) return Task.FromResult(0);

        const string sql = """
            INSERT INTO "Transactions" ("Id", "UserId", "AccountId", "Date", "PurchaseDate", "Amount",
                "OriginalDescription", "NormalizedDescription", "State", "CategoryId", "Origin", "BatchId", "SourceKey", "Ordinal",
                "CounterpartyName", "CounterpartyDocument", "IsDeleted", "EditedByUser")
            SELECT l.id, @user, @account, l.date, l.date, l.amount, l.original, l.normalized, l.state, l.category, @origin,
                @batch, l.key, 0, l.name, l.document, FALSE, FALSE
            FROM unnest(@ids, @dates, @amounts, @originals, @normalized, @states, @categories, @keys, @names, @documents)
                WITH ORDINALITY AS l(id, date, amount, original, normalized, state, category, key, name, document, n)
            ORDER BY l.n
            ON CONFLICT ("AccountId", "SourceKey", "Ordinal") WHERE "SourceKey" IS NOT NULL DO NOTHING
            """;

        object[] parameters =
        [
            new NpgsqlParameter("user", batch.UserId),
            new NpgsqlParameter("account", batch.AccountId),
            new NpgsqlParameter("origin", nameof(TransactionOrigin.Imported)),
            new NpgsqlParameter("batch", batch.Id),
            new NpgsqlParameter("ids", lines.Select(_ => Guid.NewGuid()).ToArray()),
            new NpgsqlParameter("dates", lines.Select(l => l.Date).ToArray()),
            new NpgsqlParameter("amounts", lines.Select(l => l.Amount).ToArray()),
            new NpgsqlParameter("originals", lines.Select(l => Cut(l.Memo, MaxDescription)).ToArray()),
            new NpgsqlParameter("normalized", lines.Select(l => Cut(NormalizeDescription(l.Memo), MaxDescription)).ToArray()),
            new NpgsqlParameter("states", lines.Select(l => (Override(l)?.State ?? l.State).ToString()).ToArray()),
            new NpgsqlParameter("categories", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid)
            {
                Value = lines.Select(l => Override(l)?.CategoryId).ToArray(),
            },
            new NpgsqlParameter("keys", lines.Select(l => l.FitId).ToArray()),
            new NpgsqlParameter("names", lines.Select(l => CutOrNull(l.CounterpartyName, MaxCounterpartyName)).ToArray()),
            new NpgsqlParameter("documents", lines.Select(l => CutOrNull(l.CounterpartyDocument, MaxCounterpartyDocument)).ToArray()),
        ];
        return db.Database.ExecuteSqlRawAsync(sql, parameters, ct);

        ImportLineOverride? Override(StoredPreviewLine l) => overrides.GetValueOrDefault(l.FitId);
    }

    /// <summary>LEDGERBAL checkpoint, unless the account already has one with the same date and amount.</summary>
    private async Task AddCheckpointAsync(ImportBatch batch, ImportCheckpointView checkpoint, CancellationToken ct)
    {
        // Serializes the exists-check per account across concurrent confirms (released at commit/rollback).
        var lockKey = BitConverter.ToInt64(batch.AccountId.ToByteArray(), 0);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(@key)", [new NpgsqlParameter("key", lockKey)], ct);

        var exists = await db.Checkpoints.AnyAsync(
            c => c.AccountId == batch.AccountId && c.Date == checkpoint.Date && c.Amount == checkpoint.Amount, ct);
        if (exists) return;

        db.Checkpoints.Add(new BalanceCheckpoint
        {
            Id = Guid.NewGuid(),
            AccountId = batch.AccountId,
            Date = checkpoint.Date,
            Amount = checkpoint.Amount,
            Origin = CheckpointOrigin.LedgerBal,
            BatchId = batch.Id,
            CreatedAt = time.GetUtcNow(),
        });
    }

    private static bool IsUniqueViolation(Exception e, string constraint)
    {
        for (var current = e; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == constraint)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Trim + runs of whitespace collapsed to one space.</summary>
    private static string NormalizeDescription(string text) => WhitespaceRun().Replace(text.Trim(), " ");

    private static string? CutOrNull(string? text, int max) => text is null ? null : Cut(text, max);

    /// <summary>At most <paramref name="max"/> UTF-16 units, never splitting a surrogate pair.</summary>
    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        var end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex WhitespaceRun();

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
        IReadOnlyList<StoredPreviewLine> lines, int editedCount = 0) =>
        new(batch.Id, batch.AccountId, batch.Source, batch.State, batch.PeriodStart, batch.PeriodEnd,
            batch.NewCount, batch.ExistingCount, warnings, checkpoint,
            lines.Select(l => new ImportLineView(l.FitId, l.Date, l.Amount, l.Memo, l.State, l.Existing)).ToList(),
            batch.CreatedAt, batch.ExpiresAt, editedCount);
}
