using System.Data.Common;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Infrastructure.Categorization;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinanceDashboard.Tests.Categorization;

[Collection(PostgresCollection.Name)]
public sealed class CategoryRuleServiceTests(PostgresFixture postgres)
{
    private const string User = "user-a";

    private async Task<string> NewDbAsync()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await using var m = PersistenceTestSupport.Create(cs, null);
        await m.Database.MigrateAsync();
        return cs;
    }

    private static CategoryRuleService Service(AppDbContext db, TimeProvider? time = null) =>
        new(db, time ?? TimeProvider.System);

    /// <summary>Context whose commands pass through <paramref name="hook"/> (to act between two statements).</summary>
    private static AppDbContext Hooked(string cs, string user, CommandHook hook) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).AddInterceptors(hook).Options,
            new FakeCurrentUser(user));

    /// <summary>Runs <see cref="Action"/> once, right before the first command whose text contains <see cref="Marker"/>.</summary>
    private sealed class CommandHook(string marker, Func<Task> action) : DbCommandInterceptor
    {
        private bool _fired;

        public string Marker { get; } = marker;

        public bool Fired => _fired;

        private async Task FireAsync(DbCommand command)
        {
            if (_fired || !command.CommandText.Contains(Marker, StringComparison.Ordinal)) return;
            _fired = true;
            await action();
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            await FireAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            await FireAsync(command);
            return result;
        }
    }

    /// <summary>Seeds one account, one confirmed batch and categories for a user.</summary>
    private sealed class World(AppDbContext db)
    {
        private Guid? _account;
        private Guid? _batch;

        public async Task<Category> CategoryAsync(string name, CategoryKind kind = CategoryKind.Expense)
        {
            var category = new Category { Id = Guid.NewGuid(), Name = name, Kind = kind };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return category;
        }

        public async Task<CategoryRule> RuleAsync(string pattern, Guid categoryId, int minutesAgo = 0)
        {
            var rule = new CategoryRule
            {
                Id = Guid.NewGuid(), Pattern = pattern, CategoryId = categoryId,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
            };
            db.CategoryRules.Add(rule);
            await db.SaveChangesAsync();
            return rule;
        }

        public async Task<Transaction> LineAsync(string description, decimal amount,
            TransactionState state = TransactionState.Normal, bool manual = false, bool deleted = false,
            bool edited = false, Guid? categoryId = null)
        {
            if (_account is null)
            {
                var account = new Account { Id = Guid.NewGuid(), Name = "Conta", Institution = "I", Type = AccountType.Checking };
                var batch = new ImportBatch
                {
                    Id = Guid.NewGuid(), AccountId = account.Id, Source = ImportSource.NubankChecking,
                    FileHash = Guid.NewGuid().ToString("N"), State = ImportBatchState.Confirmed, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Accounts.Add(account);
                db.ImportBatches.Add(batch);
                await db.SaveChangesAsync();
                (_account, _batch) = (account.Id, batch.Id);
            }

            var t = new Transaction
            {
                Id = Guid.NewGuid(), AccountId = _account.Value, Date = new DateOnly(2026, 10, 5), Amount = amount,
                OriginalDescription = description, NormalizedDescription = description, State = state,
                Origin = manual ? TransactionOrigin.Manual : TransactionOrigin.Imported,
                BatchId = manual ? null : _batch, IsDeleted = deleted, EditedByUser = edited, CategoryId = categoryId,
            };
            db.Transactions.Add(t);
            await db.SaveChangesAsync();
            return t;
        }
    }

    private static async Task<Transaction> ReloadAsync(AppDbContext db, Guid id) =>
        await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);

    [Fact]
    public async Task Apply_assigns_winner_to_uncategorized_imported_normal_lines()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        var compras = await w.CategoryAsync("Compras");
        await w.RuleAsync("SUPERMERCADO", compras.Id, minutesAgo: 10);
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var exemplo = await w.LineAsync("Compra no debito - Supermercado Exemplo 05/10", -50m);
        var outro = await w.LineAsync("Compra no debito - SUPERMERCADO OUTRO", -20m);
        var padaria = await w.LineAsync("Compra no debito - PADARIA FICTICIA", -5m);

        var applied = await Service(db).ApplyAsync();

        Assert.Equal(2, applied);
        Assert.Equal(mercado.Id, (await ReloadAsync(db, exemplo.Id)).CategoryId); // the longest pattern wins
        Assert.Equal(compras.Id, (await ReloadAsync(db, outro.Id)).CategoryId);
        Assert.Null((await ReloadAsync(db, padaria.Id)).CategoryId);
    }

    [Fact]
    public async Task Apply_never_overwrites_existing_category()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        var lazer = await w.CategoryAsync("Lazer");
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var line = await w.LineAsync("SUPERMERCADO EXEMPLO", -50m, categoryId: lazer.Id);

        Assert.Equal(0, await Service(db).ApplyAsync());
        Assert.Equal(lazer.Id, (await ReloadAsync(db, line.Id)).CategoryId);
    }

    [Fact]
    public async Task Apply_skips_edited_manual_hidden_pending_and_confirmed_linked_lines()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        var salario = await w.CategoryAsync("Salário", CategoryKind.Income);
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        await w.RuleAsync("EMPRESA FICTICIA", salario.Id);
        const string text = "SUPERMERCADO EXEMPLO";
        const string income = "EMPRESA FICTICIA";
        var skipped = new List<Transaction>
        {
            await w.LineAsync(text, -10m, edited: true),
            await w.LineAsync(text, -11m, manual: true),
            await w.LineAsync(text, -12m, deleted: true),
            await w.LineAsync(text, -13m, state: TransactionState.PendingClassification),
        };
        // A Confirmed pair (both columns) and a Confirmed open link (one side): neutral lines, never categorized.
        var pairOut = await w.LineAsync(text, -15m);
        var pairIn = await w.LineAsync(income, 15m);
        var open = await w.LineAsync(income, 17m);
        db.Links.AddRange(
            new Link
            {
                Id = Guid.NewGuid(), Kind = LinkKind.OwnTransfer, State = LinkState.Confirmed, OutTransactionId = pairOut.Id,
                InTransactionId = pairIn.Id, Origin = LinkOrigin.User, CreatedAt = DateTimeOffset.UtcNow,
            },
            new Link
            {
                Id = Guid.NewGuid(), Kind = LinkKind.OwnTransfer, State = LinkState.Confirmed, InTransactionId = open.Id,
                Origin = LinkOrigin.User, CreatedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();
        skipped.AddRange([pairOut, pairIn, open]);
        var control = await w.LineAsync(text, -16m);
        var controlIncome = await w.LineAsync(income, 18m);

        Assert.Equal(2, await Service(db).ApplyAsync());

        foreach (var t in skipped)
            Assert.Null((await ReloadAsync(db, t.Id)).CategoryId);
        Assert.Equal(mercado.Id, (await ReloadAsync(db, control.Id)).CategoryId);
        Assert.Equal(salario.Id, (await ReloadAsync(db, controlIncome.Id)).CategoryId);
    }

    [Fact]
    public async Task Apply_does_not_mark_edited_by_user()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var line = await w.LineAsync("SUPERMERCADO EXEMPLO", -50m);

        await Service(db).ApplyAsync();

        var after = await ReloadAsync(db, line.Id);
        Assert.Equal(mercado.Id, after.CategoryId);
        Assert.False(after.EditedByUser);
    }

    [Fact]
    public async Task Apply_is_idempotent()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var line = await w.LineAsync("SUPERMERCADO EXEMPLO", -50m);

        Assert.Equal(1, await Service(db).ApplyAsync());
        Assert.Equal(0, await Service(db).ApplyAsync());
        Assert.Equal(mercado.Id, (await ReloadAsync(db, line.Id)).CategoryId);
    }

    [Fact]
    public async Task Apply_guard_keeps_category_written_concurrently()
    {
        var cs = await NewDbAsync();
        Guid lineId, lazerId, mercadoId;
        await using (var seed = PersistenceTestSupport.Create(cs, User))
        {
            var w = new World(seed);
            mercadoId = (await w.CategoryAsync("Mercado")).Id;
            lazerId = (await w.CategoryAsync("Lazer")).Id;
            await w.RuleAsync("SUPERMERCADO EXEMPLO", mercadoId);
            lineId = (await w.LineAsync("SUPERMERCADO EXEMPLO", -50m)).Id;
        }

        // The user's category-only edit (which does not take the per-user lock) commits after the candidates were
        // read and before the lines are claimed for the update.
        var hook = new CommandHook("SKIP LOCKED", async () =>
        {
            await using var other = PersistenceTestSupport.Create(cs, User);
            await other.Transactions.Where(t => t.Id == lineId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, lazerId));
        });
        await using var db = Hooked(cs, User, hook);

        var applied = await Service(db).ApplyAsync();

        Assert.True(hook.Fired);
        Assert.Equal(0, applied);
        Assert.Equal(lazerId, (await ReloadAsync(db, lineId)).CategoryId);
    }

    [Fact]
    public async Task Apply_skips_line_being_edited_without_waiting()
    {
        var cs = await NewDbAsync();
        Guid editedId, freeId, lazerId, mercadoId;
        await using (var seed = PersistenceTestSupport.Create(cs, User))
        {
            var w = new World(seed);
            mercadoId = (await w.CategoryAsync("Mercado")).Id;
            lazerId = (await w.CategoryAsync("Lazer")).Id;
            await w.RuleAsync("SUPERMERCADO EXEMPLO", mercadoId);
            editedId = (await w.LineAsync("SUPERMERCADO EXEMPLO", -50m)).Id;
            freeId = (await w.LineAsync("SUPERMERCADO EXEMPLO", -60m)).Id;
        }

        // An edit in flight holds the row (as the edit endpoint does before it waits for the per-user lock).
        await using var other = PersistenceTestSupport.Create(cs, User);
        await using var editTx = await other.Database.BeginTransactionAsync();
        await other.Transactions.Where(t => t.Id == editedId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.CategoryId, lazerId).SetProperty(t => t.EditedByUser, true));

        await using var db = PersistenceTestSupport.Create(cs, User);
        var applied = await Service(db).ApplyAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await editTx.CommitAsync();

        Assert.Equal(1, applied);
        Assert.Equal(lazerId, (await ReloadAsync(db, editedId)).CategoryId);
        Assert.Equal(mercadoId, (await ReloadAsync(db, freeId)).CategoryId);
    }

    [Fact]
    public async Task Apply_ignores_expense_rule_on_refund()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var refund = await w.LineAsync("Estorno - SUPERMERCADO EXEMPLO", 50m);

        Assert.Equal(0, await Service(db).ApplyAsync());
        Assert.Null((await ReloadAsync(db, refund.Id)).CategoryId);
    }

    [Fact]
    public async Task Apply_survives_category_deleted_concurrently()
    {
        var cs = await NewDbAsync();
        Guid lineId, mercadoId, comprasId;
        await using (var seed = PersistenceTestSupport.Create(cs, User))
        {
            var w = new World(seed);
            mercadoId = (await w.CategoryAsync("Mercado")).Id;
            comprasId = (await w.CategoryAsync("Compras")).Id;
            await w.RuleAsync("SUPERMERCADO EXEMPLO", mercadoId);
            await w.RuleAsync("SUPERMERCADO", comprasId);
            lineId = (await w.LineAsync("SUPERMERCADO EXEMPLO", -50m)).Id;
        }

        // The category of the winning rule is deleted after the rules were read, right before they are locked.
        var hook = new CommandHook("FOR KEY SHARE", async () =>
        {
            await using var other = PersistenceTestSupport.Create(cs, User);
            await other.Categories.Where(c => c.Id == mercadoId).ExecuteDeleteAsync();
        });
        await using var db = Hooked(cs, User, hook);

        var applied = await Service(db).ApplyAsync();

        Assert.True(hook.Fired);
        Assert.Equal(1, applied);
        Assert.Equal(comprasId, (await ReloadAsync(db, lineId)).CategoryId);
    }

    [Fact]
    public async Task List_matches_counts_candidates_without_precedence()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        var compras = await w.CategoryAsync("Compras");
        var salario = await w.CategoryAsync("Salário", CategoryKind.Income);
        await w.RuleAsync("SUPERMERCADO", compras.Id);
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        await w.RuleAsync("EMPRESA FICTICIA", salario.Id);
        await w.LineAsync("SUPERMERCADO EXEMPLO", -10m);
        await w.LineAsync("SUPERMERCADO OUTRO", -11m);
        await w.LineAsync("Estorno SUPERMERCADO EXEMPLO", 12m);                // sign-incompatible
        await w.LineAsync("SUPERMERCADO EXEMPLO", -13m, categoryId: mercado.Id); // already categorized
        await w.LineAsync("SUPERMERCADO EXEMPLO", -14m, manual: true);           // not a candidate
        await w.LineAsync("SALARIO EMPRESA FICTICIA", 1000m);

        var rules = await Service(db).ListAsync();

        var byPattern = rules.ToDictionary(r => r.Pattern);
        Assert.Equal(3, rules.Count);
        Assert.Equal(2, byPattern["SUPERMERCADO"].Matches);
        Assert.Equal(1, byPattern["SUPERMERCADO EXEMPLO"].Matches);
        Assert.Equal(1, byPattern["EMPRESA FICTICIA"].Matches);
        Assert.Equal("Mercado", byPattern["SUPERMERCADO EXEMPLO"].CategoryName);
        Assert.Equal(mercado.Id, byPattern["SUPERMERCADO EXEMPLO"].CategoryId);
    }

    [Fact]
    public async Task Create_outcomes()
    {
        var cs = await NewDbAsync();
        Guid foreign;
        await using (var b = PersistenceTestSupport.Create(cs, "user-b"))
            foreign = (await new World(b).CategoryAsync("De B")).Id;

        await using var db = PersistenceTestSupport.Create(cs, User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        await w.LineAsync("Compra no debito - SUPERMERCADO EXEMPLO", -10m);
        var clock = TestClock.StartingNow();
        var service = Service(db, clock);

        var created = await service.CreateAsync("  supermercado   exemplo ", mercado.Id);
        Assert.Equal(CreateRuleOutcome.Created, created.Outcome);
        var rule = Assert.IsType<RuleListItem>(created.Rule);
        Assert.Equal("SUPERMERCADO EXEMPLO", rule.Pattern);
        Assert.Equal(mercado.Id, rule.CategoryId);
        Assert.Equal("Mercado", rule.CategoryName);
        Assert.Equal(clock.GetUtcNow(), rule.CreatedAt);
        Assert.Equal(1, rule.Matches);
        Assert.Null((await db.Transactions.AsNoTracking().SingleAsync()).CategoryId); // creating does not apply

        Assert.Equal(CreateRuleOutcome.InvalidPattern, (await service.CreateAsync("ab", mercado.Id)).Outcome);
        Assert.Equal(CreateRuleOutcome.InvalidPattern, (await service.CreateAsync("   ", mercado.Id)).Outcome);
        Assert.Equal(CreateRuleOutcome.InvalidPattern, (await service.CreateAsync(new string('A', 101), mercado.Id)).Outcome);
        // Raw input above 200 is refused even when it would collapse to a valid pattern.
        Assert.Equal(CreateRuleOutcome.InvalidPattern,
            (await service.CreateAsync("ABC" + new string(' ', 200), mercado.Id)).Outcome);
        Assert.Equal(CreateRuleOutcome.CategoryNotFound, (await service.CreateAsync("PADARIA", Guid.NewGuid())).Outcome);
        Assert.Equal(CreateRuleOutcome.CategoryNotFound, (await service.CreateAsync("PADARIA", foreign)).Outcome);
        var duplicate = await service.CreateAsync("Supermercado Exemplo", mercado.Id);
        Assert.Equal(CreateRuleOutcome.Duplicate, duplicate.Outcome);
        Assert.Null(duplicate.Rule);

        Assert.Equal(1, await db.CategoryRules.CountAsync());
    }

    [Fact]
    public async Task Create_duplicate_race_returns_Duplicate()
    {
        var cs = await NewDbAsync();
        Guid mercadoId;
        await using (var seed = PersistenceTestSupport.Create(cs, User))
            mercadoId = (await new World(seed).CategoryAsync("Mercado")).Id;

        // The other creation commits after this one checked for duplicates and before its insert.
        CreateRuleResult? first = null;
        var hook = new CommandHook("INSERT INTO \"CategoryRules\"", async () =>
        {
            await using var other = PersistenceTestSupport.Create(cs, User);
            first = await Service(other).CreateAsync("SUPERMERCADO EXEMPLO", mercadoId);
        });
        await using var db = Hooked(cs, User, hook);

        var second = await Service(db).CreateAsync("supermercado exemplo", mercadoId);

        Assert.True(hook.Fired);
        Assert.Equal(CreateRuleOutcome.Created, first?.Outcome);
        Assert.Equal(CreateRuleOutcome.Duplicate, second.Outcome);
        Assert.Equal(1, await db.CategoryRules.CountAsync());
        Assert.Single(await Service(db).ListAsync()); // the context stays usable
    }

    [Fact]
    public async Task Delete_does_not_undo_assigned_categories()
    {
        var cs = await NewDbAsync();
        await using var db = PersistenceTestSupport.Create(cs, User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        var rule = await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var line = await w.LineAsync("SUPERMERCADO EXEMPLO", -50m);
        await Service(db).ApplyAsync();

        await using (var b = PersistenceTestSupport.Create(cs, "user-b"))
            Assert.False(await Service(b).DeleteAsync(rule.Id)); // another user's rule: not found

        Assert.True(await Service(db).DeleteAsync(rule.Id));
        Assert.False(await Service(db).DeleteAsync(rule.Id));
        Assert.Empty(await db.CategoryRules.ToListAsync());
        Assert.Equal(mercado.Id, (await ReloadAsync(db, line.Id)).CategoryId);
        Assert.Equal(0, await Service(db).ApplyAsync());
    }

    [Fact]
    public async Task Apply_with_batch_only_touches_that_batch()
    {
        await using var db = PersistenceTestSupport.Create(await NewDbAsync(), User);
        var w = new World(db);
        var mercado = await w.CategoryAsync("Mercado");
        await w.RuleAsync("SUPERMERCADO EXEMPLO", mercado.Id);
        var inBatch = await w.LineAsync("SUPERMERCADO EXEMPLO", -50m);
        var otherBatch = new ImportBatch
        {
            Id = Guid.NewGuid(), AccountId = inBatch.AccountId, Source = ImportSource.NubankChecking,
            FileHash = Guid.NewGuid().ToString("N"), State = ImportBatchState.Confirmed, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ImportBatches.Add(otherBatch);
        var outside = new Transaction
        {
            Id = Guid.NewGuid(), AccountId = inBatch.AccountId, Date = new DateOnly(2026, 10, 6), Amount = -9m,
            OriginalDescription = "SUPERMERCADO EXEMPLO", NormalizedDescription = "SUPERMERCADO EXEMPLO",
            State = TransactionState.Normal, Origin = TransactionOrigin.Imported, BatchId = otherBatch.Id,
        };
        db.Transactions.Add(outside);
        await db.SaveChangesAsync();

        Assert.Equal(1, await Service(db).ApplyAsync(inBatch.BatchId));
        Assert.Equal(mercado.Id, (await ReloadAsync(db, inBatch.Id)).CategoryId);
        Assert.Null((await ReloadAsync(db, outside.Id)).CategoryId);
    }
}
