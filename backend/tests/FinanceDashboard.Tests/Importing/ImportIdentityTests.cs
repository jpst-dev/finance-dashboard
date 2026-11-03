using System.Text.Json;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.Importing;

/// <summary>
/// Line identity is the pair (SourceKey, Ordinal) from preview to insert. The service is driven directly with
/// synthetic <see cref="ParsedStatement"/>s (no file), against the real Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportIdentityTests(PostgresFixture postgres)
{
    private const string User = "user-identity";
    private const string Key = "K";
    private static readonly DateOnly Day = new(2026, 10, 5);

    private string _cs = "";

    private async Task<Guid> NewAccountAsync(AccountType type = AccountType.Checking)
    {
        if (_cs.Length == 0)
        {
            _cs = await postgres.CreateDatabaseAsync();
            await using var m = PersistenceTestSupport.Create(_cs, null);
            await m.Database.MigrateAsync();
        }

        await using var db = Db();
        var account = new Account { Id = Guid.NewGuid(), Name = "Conta", Institution = "Banco", Type = type };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private AppDbContext Db() => PersistenceTestSupport.Create(_cs, User);

    private static ParsedLine Line(
        int ordinal, string key = Key, decimal amount = -10m, DateOnly? date = null, string memo = "PIX QRS PADARIA FICTICIA",
        TransactionState state = TransactionState.Normal) =>
        new(key, ordinal, date ?? Day, amount, memo, state);

    private static ParsedStatement Statement(params ParsedLine[] lines) => Statement(ImportSource.ItauChecking, lines);

    private static ParsedStatement Statement(ImportSource source, params ParsedLine[] lines) =>
        new(source, null, null, null, lines, null);

    private async Task<ImportPreviewView> PreviewAsync(Guid accountId, ParsedStatement parsed)
    {
        await using var db = Db();
        var outcome = await new ImportService(db, TimeProvider.System)
            .CreatePreviewAsync(accountId, parsed, Guid.NewGuid().ToString("N"));
        Assert.Null(outcome.Error);
        return outcome.Preview!;
    }

    private async Task<ConfirmOutcome> ConfirmAsync(
        Guid batchId, IReadOnlyList<ImportLineOverride>? overrides = null, bool acknowledge = false)
    {
        await using var db = Db();
        return await new ImportService(db, TimeProvider.System).ConfirmAsync(batchId, acknowledge, overrides);
    }

    private async Task<List<Transaction>> TransactionsAsync(Guid accountId)
    {
        await using var db = Db();
        return await db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId)
            .OrderBy(t => t.SourceKey).ThenBy(t => t.Ordinal).ToListAsync();
    }

    private async Task SeedAsync(Transaction t)
    {
        await using var db = Db();
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
    }

    private static Transaction Row(
        Guid accountId, string? key, int ordinal = 0, decimal amount = -10m, DateOnly? date = null,
        string description = "PIX QRS PADARIA X", bool deleted = false) => new()
    {
        Id = Guid.NewGuid(), AccountId = accountId, Date = date ?? Day, Amount = amount,
        OriginalDescription = description, NormalizedDescription = description, SourceKey = key, Ordinal = ordinal,
        Origin = key is null ? TransactionOrigin.Manual : TransactionOrigin.Imported, IsDeleted = deleted,
    };

    [Fact]
    public async Task Same_key_different_ordinals_are_all_created()
    {
        var acc = await NewAccountAsync();
        var preview = await PreviewAsync(acc, Statement(Line(0), Line(1), Line(2)));
        Assert.Equal(3, preview.NewCount);

        var outcome = await ConfirmAsync(preview.BatchId);

        Assert.Null(outcome.Error);
        Assert.Equal(3, outcome.Result!.Created);
        Assert.Equal([0, 1, 2], (await TransactionsAsync(acc)).Select(t => t.Ordinal));
    }

    [Fact]
    public async Task Existing_is_decided_per_pair()
    {
        var acc = await NewAccountAsync();
        var first = await PreviewAsync(acc, Statement(Line(0), Line(1)));
        Assert.Null((await ConfirmAsync(first.BatchId)).Error);

        var second = await PreviewAsync(acc, Statement(Line(0), Line(1), Line(2)));

        Assert.Equal([true, true, false], second.Lines.Select(l => l.Existing));
        Assert.Equal(1, second.NewCount);
        Assert.Equal(2, second.ExistingCount);
    }

    [Fact]
    public async Task Ordinal_holes_are_reported_honestly()
    {
        var acc = await NewAccountAsync();
        await SeedAsync(Row(acc, Key, ordinal: 1));

        var preview = await PreviewAsync(acc, Statement(Line(0), Line(1)));
        Assert.Equal([false, true], preview.Lines.Select(l => l.Existing));

        var outcome = await ConfirmAsync(preview.BatchId);
        Assert.Equal(1, outcome.Result!.Created);
        Assert.Equal([0, 1], (await TransactionsAsync(acc)).Select(t => t.Ordinal));
    }

    [Fact]
    public async Task Override_targets_one_ordinal()
    {
        var acc = await NewAccountAsync();
        var pending = TransactionState.PendingClassification;
        var preview = await PreviewAsync(acc,
            Statement(Line(0, state: pending), Line(1, state: pending), Line(2, state: pending)));

        var outcome = await ConfirmAsync(preview.BatchId, [new ImportLineOverride(Key, TransactionState.Normal, Ordinal: 1)]);

        Assert.Null(outcome.Error);
        Assert.Equal([pending, TransactionState.Normal, pending], (await TransactionsAsync(acc)).Select(t => t.State));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public async Task Override_with_unknown_or_negative_ordinal_is_invalid(int ordinal)
    {
        var acc = await NewAccountAsync();
        var preview = await PreviewAsync(acc, Statement(Line(0), Line(1)));

        var outcome = await ConfirmAsync(preview.BatchId,
            [new ImportLineOverride(Key, TransactionState.PendingClassification, Ordinal: ordinal)]);

        Assert.Equal(ConfirmError.InvalidOverride, outcome.Error);
        Assert.Empty(await TransactionsAsync(acc));
        await using var db = Db();
        Assert.Equal(ImportBatchState.Preview, (await db.ImportBatches.SingleAsync(b => b.Id == preview.BatchId)).State);
    }

    [Fact]
    public async Task Repeated_pair_in_overrides_is_invalid_but_sibling_ordinal_is_fine()
    {
        var acc = await NewAccountAsync();
        var preview = await PreviewAsync(acc, Statement(Line(0), Line(1)));
        var pending = TransactionState.PendingClassification;

        var repeated = await ConfirmAsync(preview.BatchId,
            [new ImportLineOverride(Key, pending, Ordinal: 0), new ImportLineOverride(Key, pending, Ordinal: 0)]);
        Assert.Equal(ConfirmError.InvalidOverride, repeated.Error);
        Assert.Empty(await TransactionsAsync(acc));

        var siblings = await ConfirmAsync(preview.BatchId,
            [new ImportLineOverride(Key, pending, Ordinal: 0), new ImportLineOverride(Key, pending, Ordinal: 1)]);
        Assert.Null(siblings.Error);
        Assert.All(await TransactionsAsync(acc), t => Assert.Equal(pending, t.State));
    }

    [Fact]
    public async Task Key_in_other_account_blocks_only_nubank()
    {
        var acc = await NewAccountAsync();
        var other = await NewAccountAsync();
        await SeedAsync(Row(other, Key, amount: -99m));

        var itau = await PreviewAsync(acc, Statement(Line(0)));
        Assert.DoesNotContain(itau.Warnings, w => w.Code == "FitidInOtherAccount");
        Assert.Null((await ConfirmAsync(itau.BatchId)).Error);

        var acc2 = await NewAccountAsync();
        var nubank = await PreviewAsync(acc2, Statement(ImportSource.NubankChecking, Line(0, memo: "Compra no debito")));
        var warning = Assert.Single(nubank.Warnings, w => w.Code == "FitidInOtherAccount");
        Assert.Equal(ImportWarning.Blocking, warning.Severity);
        var refused = await ConfirmAsync(nubank.BatchId);
        Assert.Equal(ConfirmError.BlockingWarnings, refused.Error);
        Assert.Contains(refused.Warnings, w => w.Code == "FitidInOtherAccount");
    }

    [Fact]
    public async Task Possible_duplicate_warns_on_amount_date_and_prefix()
    {
        var acc = await NewAccountAsync();
        await SeedAsync(Row(acc, null, amount: -10m, date: new DateOnly(2026, 10, 5), description: "PIX QRS PADARIA X"));

        var preview = await PreviewAsync(acc,
            Statement(Line(0, amount: -10m, date: new DateOnly(2026, 10, 6), memo: "PIX QRS PADARIA Y")));

        var warning = Assert.Single(preview.Warnings, w => w.Code == "PossibleDuplicate");
        Assert.Equal(ImportWarning.Warn, warning.Severity);
        Assert.Equal(ImportWarning.PossibleDuplicate(1), warning);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("two_days")]
    [InlineData("prefix")]
    [InlineData("same_key")]
    [InlineData("siblings")]
    [InlineData("soft_deleted")]
    public async Task Possible_duplicate_is_not_raised(string variant)
    {
        var acc = await NewAccountAsync();
        var next = new DateOnly(2026, 10, 6);
        ParsedStatement parsed;
        switch (variant)
        {
            case "amount":
                await SeedAsync(Row(acc, null, amount: -11m));
                parsed = Statement(Line(0, date: next, memo: "PIX QRS PADARIA Y"));
                break;
            case "two_days":
                await SeedAsync(Row(acc, null));
                parsed = Statement(Line(0, date: new DateOnly(2026, 10, 7), memo: "PIX QRS PADARIA Y"));
                break;
            case "prefix":
                await SeedAsync(Row(acc, null));
                parsed = Statement(Line(0, date: next, memo: "PIX QRS MERCADO Y"));
                break;
            case "same_key":
                // The stored K#0 is this file's own key: K#1 is a legitimate second occurrence, not a duplicate.
                await SeedAsync(Row(acc, Key, ordinal: 0));
                parsed = Statement(Line(0, memo: "PIX QRS PADARIA X"), Line(1, date: next, memo: "PIX QRS PADARIA Y"));
                break;
            case "siblings":
                parsed = Statement(Line(0, memo: "PIX QRS PADARIA X"), Line(1, date: next, memo: "PIX QRS PADARIA Y"));
                break;
            default:
                await SeedAsync(Row(acc, null, deleted: true));
                parsed = Statement(Line(0, date: next, memo: "PIX QRS PADARIA Y"));
                break;
        }

        var preview = await PreviewAsync(acc, parsed);

        Assert.DoesNotContain(preview.Warnings, w => w.Code == "PossibleDuplicate");
    }

    [Fact]
    public async Task Metadata_reaches_transactions()
    {
        var acc = await NewAccountAsync(AccountType.CreditCard);
        var purchase = new DateOnly(2026, 8, 3);
        var statement = new DateOnly(2026, 11, 10);
        var line = Line(0, amount: -120m, date: new DateOnly(2026, 10, 10), memo: "LOJA FICTICIA") with
        {
            InstallmentNumber = 2, InstallmentTotal = 10, CardSuffix = "1234", CardKind = CardKind.Physical,
            PurchaseDate = purchase, StatementDate = statement,
        };

        var preview = await PreviewAsync(acc, Statement(ImportSource.ItauCard, line));

        var json = JsonSerializer.SerializeToElement(preview.Lines[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(0, json.GetProperty("ordinal").GetInt32());
        Assert.Equal(2, json.GetProperty("installmentNumber").GetInt32());
        Assert.Equal(10, json.GetProperty("installmentTotal").GetInt32());
        Assert.Equal("1234", json.GetProperty("cardSuffix").GetString());
        Assert.True(json.TryGetProperty("cardKind", out _));
        Assert.Equal("2026-08-03", json.GetProperty("purchaseDate").GetString());
        Assert.Equal(CardKind.Physical, preview.Lines[0].CardKind);

        Assert.Null((await ConfirmAsync(preview.BatchId)).Error);

        var t = Assert.Single(await TransactionsAsync(acc));
        Assert.Equal(2, t.InstallmentNumber);
        Assert.Equal(10, t.InstallmentTotal);
        Assert.Equal("1234", t.CardSuffix);
        Assert.Equal(CardKind.Physical, t.CardKind);
        Assert.Equal(purchase, t.PurchaseDate);
        Assert.Equal(statement, t.StatementDate);
        Assert.Equal(new DateOnly(2026, 10, 10), t.Date);
    }

    [Fact]
    public async Task Legacy_preview_json_confirms_with_defaults()
    {
        var acc = await NewAccountAsync();
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(), AccountId = acc, Source = ImportSource.NubankChecking, FileHash = Guid.NewGuid().ToString("N"),
            State = ImportBatchState.Preview, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            PreviewJson = """{"accountExternalId":null,"checkpoint":null,"lines":[{"fitId":"X","date":"2026-10-05","amount":-10.00,"memo":"M","state":"Normal","counterpartyName":null,"counterpartyDocument":null,"existing":false}]}""",
            WarningsJson = "[]", NewCount = 1,
        };
        await using (var db = Db())
        {
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        var outcome = await ConfirmAsync(batch.Id);

        Assert.Null(outcome.Error);
        Assert.Equal(1, outcome.Result!.Created);
        var t = Assert.Single(await TransactionsAsync(acc));
        Assert.Equal("X", t.SourceKey);
        Assert.Equal(0, t.Ordinal);
        Assert.Equal(new DateOnly(2026, 10, 5), t.PurchaseDate);
        Assert.Null(t.StatementDate);
        Assert.Null(t.InstallmentNumber);
        Assert.Null(t.InstallmentTotal);
        Assert.Null(t.CardSuffix);
        Assert.Null(t.CardKind);
    }

    [Fact]
    public async Task Reader_warnings_and_batch_summary_fields_are_kept()
    {
        var acc = await NewAccountAsync();
        // The line matches the declared total: confirm re-checks it (InvoiceTotalMismatch) from the stored preview.
        var parsed = Statement(Line(0, amount: -120.00m)) with
        {
            Warnings = [ImportWarning.UnknownLineKinds(2)],
            Invoice = new ParsedInvoice(new DateOnly(2026, 11, 10), 120.00m),
            LedgerRead = new ParsedBalance(500.00m, new DateOnly(2026, 10, 31)),
        };

        var preview = await PreviewAsync(acc, parsed);

        Assert.Contains(ImportWarning.UnknownLineKinds(2), preview.Warnings);
        Assert.Equal(new ImportInvoiceView(new DateOnly(2026, 11, 10), 120.00m), preview.Invoice);
        await using (var db = Db())
        {
            var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == preview.BatchId);
            Assert.Equal(new DateOnly(2026, 11, 10), batch.DueDate);
            Assert.Equal(120.00m, batch.DeclaredTotal);
            Assert.Equal(500.00m, batch.LedgerBalance);
            Assert.Equal(new DateOnly(2026, 10, 31), batch.LedgerBalanceDate);

            var summary = Assert.Single(await new ImportService(db, TimeProvider.System).ListAsync());
            Assert.Equal(new ImportInvoiceView(new DateOnly(2026, 11, 10), 120.00m), summary.Invoice);
        }

        Assert.Null((await ConfirmAsync(preview.BatchId)).Error);
        await using (var db = Db())
        {
            var view = await new ImportService(db, TimeProvider.System).GetAsync(preview.BatchId);
            Assert.Contains(ImportWarning.UnknownLineKinds(2), view!.Warnings);
            Assert.Equal(new ImportInvoiceView(new DateOnly(2026, 11, 10), 120.00m), view.Invoice);
        }
    }
}
