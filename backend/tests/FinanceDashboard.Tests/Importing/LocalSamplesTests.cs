using System.Net;
using System.Text.RegularExpressions;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>
/// Runs only on a developer machine that has the (git-ignored) finance-samples/ folder; skipped in CI.
/// Assertion messages are static on purpose: real statement content must never reach test output.
/// </summary>
public sealed class LocalSamplesFactAttribute : FactAttribute
{
    public LocalSamplesFactAttribute()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "CI: real samples are never used.";
        }
        else if (LocalSamples.Directory() is null)
        {
            Skip = "finance-samples/ not found.";
        }
    }
}

internal static class LocalSamples
{
    public static string? Directory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "finance-samples");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            if (System.IO.Directory.Exists(Path.Combine(dir.FullName, ".git"))) return null;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Every .ofx and .xlsx sample. File names never reach assertions or output.</summary>
    public static List<string> Files() =>
        System.IO.Directory.EnumerateFiles(Directory()!, "*.ofx")
            .Concat(System.IO.Directory.EnumerateFiles(Directory()!, "*.xlsx"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Reads a sample. On a format error the test fails with the error kind only (a fixed code such as
    /// <c>xlsx_unsafe_content</c>), never the file name or content.
    /// </summary>
    public static ParsedStatement Read(string file)
    {
        try
        {
            return StatementReader.Read(File.ReadAllBytes(file));
        }
        catch (ImportFormatException e)
        {
            Assert.Fail("a sample was rejected by the reader: " + e.Kind);
            throw;
        }
    }

    public static bool IsItauChecking(ParsedStatement s) => s.Source == ImportSource.ItauChecking;
    public static bool IsItauInvoice(ParsedStatement s) => s.Source == ImportSource.ItauCard;
}

public sealed class LocalSamplesTests
{
    private static readonly Regex Hex64 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    [LocalSamplesFact]
    public void Real_samples_parse_and_roundtrip()
    {
        var files = LocalSamples.Files();
        var sources = new HashSet<ImportSource>();

        foreach (var file in files)
        {
            var parsed = LocalSamples.Read(file);

            Assert.True(parsed.Lines.Count > 0, "a sample has no lines");
            Assert.True(parsed.Lines.All(l => !string.IsNullOrEmpty(l.SourceKey)), "a sample line has no source key");
            Assert.True(parsed.Lines.All(l => l.Date.Year is >= 2000 and <= 2100), "a sample line has an implausible date");
            Assert.True(Enum.IsDefined(parsed.Source), "unknown statement kind");
            Assert.True(parsed.Lines.Select(l => (l.SourceKey, l.Ordinal)).Distinct().Count() == parsed.Lines.Count,
                "a sample has duplicate line identities");
            sources.Add(parsed.Source);
        }

        Assert.True(files.Count > 0, "no samples found");
        Assert.True(sources.Count > 0, "no statement kind recognized");
    }

    [LocalSamplesFact]
    public void Real_itau_checking_samples_are_consistent()
    {
        var seen = 0;
        foreach (var file in LocalSamples.Files().Where(f => f.EndsWith(".ofx", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = File.ReadAllBytes(file);
            var parsed = LocalSamples.Read(file);
            if (!LocalSamples.IsItauChecking(parsed)) continue;
            seen++;

            Assert.True(parsed.Lines.Count > 0, "an Itaú checking sample has no lines");
            Assert.True(parsed.Lines.All(l => l.Date.Year is >= 2000 and <= 2100), "an Itaú checking line has an implausible date");
            Assert.True(parsed.Lines.All(l => Hex64.IsMatch(l.SourceKey)), "an Itaú checking line has no 64-hex key");
            Assert.True(OrdinalsAreContiguous(parsed.Lines), "Itaú checking ordinals are not contiguous per key");

            var document = OfxReader.Parse(bytes);
            Assert.True(document.Transactions.Count == parsed.Lines.Count, "Itaú checking line count differs from the OFX");
            foreach (var t in document.Transactions)
            {
                var coherent = t.TrnType switch
                {
                    "CREDIT" => t.Amount > 0,
                    "DEBIT" => t.Amount < 0,
                    _ => false,
                };
                Assert.True(coherent, "an Itaú checking TRNTYPE disagrees with the amount sign");
            }
        }

        Assert.True(seen > 0, "no Itaú checking sample found");
    }

    [LocalSamplesFact]
    public void Real_itau_invoices_have_matching_totals()
    {
        var seen = 0;
        foreach (var file in LocalSamples.Files().Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
        {
            var parsed = LocalSamples.Read(file); // fails with the kind only (invalid_cell, unsupported_layout, …)
            Assert.True(LocalSamples.IsItauInvoice(parsed), "an xlsx sample was not read as an Itaú invoice");
            seen++;

            Assert.True(parsed.Lines.Count > 0, "an invoice sample has no lines");
            Assert.NotNull(parsed.Invoice);
            Assert.True(parsed.Warnings.All(w => w.Code != "InvoiceTotalMismatch"), "an invoice sample has a total mismatch");
            Assert.True(parsed.Lines.All(l => Hex64.IsMatch(l.SourceKey)), "an invoice line has no 64-hex key");
            Assert.True(OrdinalsAreContiguous(parsed.Lines), "invoice ordinals are not contiguous per key");
            Assert.True(parsed.Lines.All(l => l.StatementDate == parsed.Invoice!.DueDate), "an invoice line has another due date");
            Assert.True(
                parsed.Lines.Where(l => l.InstallmentNumber > 1).All(l => l.Date == parsed.Invoice!.DueDate.AddMonths(-1)),
                "an installment after the first is not dated one month before the due date");
            Assert.True(parsed.Lines.All(l => l.Date.Year is >= 2000 and <= 2100), "an invoice line has an implausible date");
        }

        Assert.True(seen > 0, "no invoice sample found");
    }

    private static bool OrdinalsAreContiguous(IEnumerable<ParsedLine> lines) =>
        lines.GroupBy(l => l.SourceKey).All(g =>
            g.Select(l => l.Ordinal).Order().SequenceEqual(Enumerable.Range(0, g.Count())));
}

/// <summary>Preview + confirm of every real sample in an account of the right type, twice: the second creates nothing.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LocalSamplesReimportTests(PostgresFixture postgres)
{
    [LocalSamplesFact]
    public async Task Real_samples_reimport_is_noop()
    {
        var files = LocalSamples.Files();
        Assert.True(files.Count > 0, "no samples found");

        foreach (var file in files)
        {
            // One database per sample: Nubank and Itaú files of the same period must not meet each other's checks.
            await using var f = new ApiFactory(await postgres.CreateDatabaseAsync());
            using var c = await AuthedClient.CreateAsync(f, "local@example.com", Password);

            var bytes = File.ReadAllBytes(file);
            var parsed = LocalSamples.Read(file);
            var isCard = parsed.Source is ImportSource.NubankCard or ImportSource.ItauCard;
            var uploadName = file.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? "amostra.xlsx" : "amostra.ofx";
            var account = await CreateAccountAsync(c, isCard ? "CreditCard" : "Checking");

            var first = await UploadAsync(c, account, bytes, uploadName);
            Assert.True(first.StatusCode == HttpStatusCode.OK, "the first preview of a sample failed");
            var firstConfirm = await ConfirmAsync(c, (await JsonOf(first)).GetProperty("batchId").GetGuid());
            Assert.True(firstConfirm.StatusCode == HttpStatusCode.OK, "the first confirm of a sample failed");
            var created = (await JsonOf(firstConfirm)).GetProperty("created").GetInt32();
            Assert.True(created == parsed.Lines.Count, "the first confirm did not create every line");

            var second = await UploadAsync(c, account, bytes, uploadName);
            Assert.True(second.StatusCode == HttpStatusCode.OK, "the second preview of a sample failed");
            var secondBody = await JsonOf(second);
            Assert.True(secondBody.GetProperty("newCount").GetInt32() == 0, "the second preview of a sample has new lines");

            // Same bytes = same file hash: the confirm is refused (409 already_imported) and nothing is written; any
            // other outcome must be a 200 that creates nothing.
            var secondConfirm = await ConfirmAsync(c, secondBody.GetProperty("batchId").GetGuid());
            if (secondConfirm.StatusCode == HttpStatusCode.Conflict)
                Assert.True(await ErrorOf(secondConfirm) == "already_imported", "the second confirm of a sample failed");
            else
            {
                Assert.True(secondConfirm.StatusCode == HttpStatusCode.OK, "the second confirm of a sample failed");
                Assert.True((await JsonOf(secondConfirm)).GetProperty("created").GetInt32() == 0, "re-importing a sample created lines");
            }

            var stored = await CountTransactionsAsync(f, c);
            Assert.True(stored == parsed.Lines.Count, "re-importing a sample changed the stored lines");
        }
    }

    private static async Task<int> CountTransactionsAsync(ApiFactory f, AuthedClient c)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        return await db.Transactions.AsNoTracking().CountAsync(t => !t.IsDeleted);
    }
}
