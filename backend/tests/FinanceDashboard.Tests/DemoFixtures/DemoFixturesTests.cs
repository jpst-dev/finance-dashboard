using System.Globalization;
using System.Text.Json.Nodes;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.DemoFixtures;

[Collection(PostgresCollection.Name)]
public sealed class DemoFixturesTests(PostgresFixture postgres)
{
    [Fact]
    public void DemoJson_normalizes_line_endings_and_trailing_newline()
    {
        Assert.Equal("{\n  \"a\": 1\n}\n", DemoJson.Normalize("{\r\n\"a\":1}"));
    }

    [Fact]
    public void SortByCategory_orders_by_id_with_null_last()
    {
        var summary = JsonNode.Parse("""
            {"month":"2026-10","byCategory":[
              {"categoryId":null,"total":"-1.00"},
              {"categoryId":"0000000c-0000-4000-8000-000000000002","total":"-2.00"},
              {"categoryId":"0000000c-0000-4000-8000-000000000001","total":"-3.00"}]}
            """)!;

        var sorted = DemoJson.SortByCategory(summary)["byCategory"]!.AsArray()
            .Select(c => c!["categoryId"]?.GetValue<string>()).ToList();

        Assert.Equal(
            ["0000000c-0000-4000-8000-000000000001", "0000000c-0000-4000-8000-000000000002", null],
            sorted);
    }

    [Fact]
    public async Task Seed_covers_every_default_category()
    {
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync());
        await DemoSeed.SeedAsync(f);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, DemoSeed.UserId);
        Assert.Equal(13, await db.Categories.CountAsync());
        Assert.All(await db.Categories.ToListAsync(), c => Assert.True(c.IsDefault));
        await DefaultCategories.EnsureForUserAsync(db, DemoSeed.UserId);
        Assert.Equal(13, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Seed_has_no_line_after_reference_date_and_covers_every_month()
    {
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync());
        await DemoSeed.SeedAsync(f);

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, DemoSeed.UserId);
        var lines = await db.Transactions.AsNoTracking().ToListAsync();
        var cards = await db.Accounts.Where(a => a.Type == AccountType.CreditCard).Select(a => a.Id).ToListAsync();

        Assert.True(lines.Count >= 60, $"only {lines.Count} lines");
        Assert.DoesNotContain(lines, t => t.IsDeleted);
        Assert.True(lines.Max(t => t.Date) <= DemoSeed.ReferenceDate);
        foreach (var month in DemoSeed.Months)
        {
            var inMonth = lines.Where(t => t.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture) == month
                                           && t.State == TransactionState.Normal).ToList();
            Assert.Contains(inMonth, t => t.Amount > 0 && !cards.Contains(t.AccountId));
            Assert.Contains(inMonth, t => t.Amount < 0);
            Assert.True(inMonth.Where(t => t.Amount < 0).Select(t => t.CategoryId).Distinct().Count() > 1, month);
        }

        // Manual pending lines (the link-candidate lines below come from imports and are asserted in the links test).
        var manualPending = lines.Where(t => t.State == TransactionState.PendingClassification && t.BatchId is null).ToList();
        Assert.Equal(3, manualPending.Count);
        Assert.Equal(2, manualPending.Count(t => t.Date.Month == 10));
        Assert.Contains(lines, t => t.State == TransactionState.Normal && t.CategoryId is null);
        Assert.Contains(lines, t => t.EditedByUser);
        Assert.Equal([1, 2, 3], lines.Where(t => t.InstallmentTotal == 3).OrderBy(t => t.Date).Select(t => t.InstallmentNumber ?? 0));

        // The confirmed invoice is chronologically coherent: its lines precede the import and the due date.
        var invoice = await db.ImportBatches.SingleAsync(b => b.Id == DemoSeed.ConfirmedBatchId);
        var invoiceLines = lines.Where(t => t.BatchId == invoice.Id).ToList();
        Assert.NotEmpty(invoiceLines);
        Assert.All(invoiceLines, t => Assert.True(
            t.Date <= DateOnly.FromDateTime(invoice.CreatedAt.UtcDateTime) && t.Date < invoice.DueDate!.Value));
        Assert.True(invoice.DueDate <= DemoSeed.ReferenceDate);
        Assert.Equal(invoice.DeclaredTotal, -invoiceLines.Where(t => t.State == TransactionState.Normal).Sum(t => t.Amount));
        Assert.Contains(invoiceLines, t => t.InstallmentNumber == 3);
    }

    [Fact]
    public async Task Generated_links_cover_each_kind_and_a_suggestion_from_the_real_matcher()
    {
        var files = await DemoFixtureGenerator.GenerateAsync(postgres);

        var links = JsonNode.Parse(files["links.json"])!;
        var confirmed = links["confirmed"]!.AsArray();
        var suggested = links["suggested"]!.AsArray();
        Assert.Equal(new[] { "InvoicePayment", "OwnTransfer" }, confirmed.Select(l => l!["kind"]!.GetValue<string>()).Order().ToArray());
        Assert.Equal(new[] { "OwnTransfer" }, suggested.Select(l => l!["kind"]!.GetValue<string>()).ToArray());
        Assert.All(confirmed, l => Assert.Equal("Auto", l!["origin"]!.GetValue<string>()));

        // Ids are the deterministic fixture ids and the lines' badge points at the same link.
        var ids = confirmed.Concat(suggested).Select(l => l!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(Enumerable.Range(1, 3).Select(n => DemoSeed.FixtureLinkId(n).ToString()), ids);
        var badges = JsonNode.Parse(files["transactions.json"])!.AsArray()
            .Where(t => t!["link"] is not null).Select(t => t!["link"]!["id"]!.GetValue<string>()).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(ids.Order(StringComparer.Ordinal).ToList(), badges);
    }

    [Fact]
    public async Task Generation_is_deterministic_across_cultures()
    {
        var first = await DemoFixtureGenerator.GenerateAsync(postgres);

        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        IReadOnlyDictionary<string, string> second;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
            second = await DemoFixtureGenerator.GenerateAsync(postgres);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }

        Assert.Equal(first.Keys.Order(StringComparer.Ordinal), second.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, content) in first) Assert.Equal(content, second[name]);
    }

    [Fact]
    public async Task Generated_preview_matches_seed()
    {
        var files = await DemoFixtureGenerator.GenerateAsync(postgres);

        var preview = JsonNode.Parse(files["import-details.json"])![DemoSeed.PreviewBatchId.ToString()]!;
        Assert.Equal("Preview", preview["state"]!.GetValue<string>());
        var lines = preview["lines"]!.AsArray();
        Assert.Equal(6, lines.Count);
        Assert.Equal(3, lines.Count(l => l!["existing"]!.GetValue<bool>()));
        Assert.Contains(preview["warnings"]!.AsArray(), w => w!["code"]!.GetValue<string>() == "PossibleDuplicate");

        var states = JsonNode.Parse(files["imports.json"])!.AsArray().Select(b => b!["state"]!.GetValue<string>()).Order().ToList();
        Assert.Equal(["Confirmed", "Confirmed", "Confirmed", "Confirmed", "Preview"], states);
    }

    [Fact]
    public async Task Committed_fixtures_match_generator()
    {
        var generated = await DemoFixtureGenerator.GenerateAsync(postgres);
        var dir = DemoFixtureGenerator.FixturesDir();

        if (Environment.GetEnvironmentVariable("UPDATE_DEMO_FIXTURES") == "1")
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            {
                Assert.Fail("UPDATE_DEMO_FIXTURES não pode rodar na CI");
            }

            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.json")) File.Delete(old);
            foreach (var (name, content) in generated) await File.WriteAllTextAsync(Path.Combine(dir, name), content);
            return;
        }

        var committed = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList()
            : [];
        Assert.True(generated.Keys.Order(StringComparer.Ordinal).SequenceEqual(committed),
            $"Fixtures do demo divergem do gerador: rode UPDATE_DEMO_FIXTURES=1 ({string.Join(", ", committed)})");
        foreach (var (name, content) in generated)
        {
            var onDisk = (await File.ReadAllTextAsync(Path.Combine(dir, name))).Replace("\r\n", "\n");
            Assert.True(content == onDisk, $"Fixtures do demo divergem do gerador: rode UPDATE_DEMO_FIXTURES=1 ({name})");
        }
    }
}
