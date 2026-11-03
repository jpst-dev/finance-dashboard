using System.IO.Compression;
using System.Net;
using System.Text.Json;
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

    public static ParsedStatement Read(string file)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception e)
        {
            Assert.Fail("a sample could not be read: " + e.GetType().Name);
            throw;
        }
        return Read(bytes);
    }

    /// <summary>
    /// Reads sample bytes. On a format error the test fails with the error kind only (a fixed code such as
    /// <c>xlsx_unsafe_content</c>); on any other exception, with the exception type name only. Never the file name,
    /// the exception message or content.
    /// </summary>
    public static ParsedStatement Read(byte[] content)
    {
        try
        {
            return StatementReader.Read(content);
        }
        catch (ImportFormatException e)
        {
            Assert.Fail("a sample was rejected by the reader: " + e.Kind);
            throw;
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            Assert.Fail("a sample made the reader throw: " + e.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// The same content in different bytes, like a new download of the same statement. OFX: a trailing line break
    /// after <c>&lt;/OFX&gt;</c> (the reader ignores it). xlsx: the zip rewritten entry by entry, same order and
    /// contents, with a new timestamp (what Excel/the bank does on every download).
    /// </summary>
    public static byte[] ReExport(byte[] content, bool isXlsx)
    {
        if (!isXlsx) return [.. content, (byte)'\r', (byte)'\n'];

        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var stamp = new DateTimeOffset(2030, 1, 2, 3, 4, 6, TimeSpan.Zero);
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = stamp;
                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }
        }
        return output.ToArray();
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
        ImportSource[] known = [ImportSource.NubankChecking, ImportSource.NubankCard, ImportSource.ItauChecking, ImportSource.ItauCard];
        Assert.True(sources.All(known.Contains), "a sample was read as an unexpected statement kind");
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

/// <summary>
/// Every real sample imported in an account of the right type, then sent again twice: the identical bytes are
/// refused by the file hash, and a re-export (same content, other bytes) is matched line by line and creates nothing.
/// </summary>
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
            var isXlsx = file.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            var uploadName = isXlsx ? "amostra.xlsx" : "amostra.ofx";
            var account = await CreateAccountAsync(c, isCard ? "CreditCard" : "Checking");

            var first = await UploadAsync(c, account, bytes, uploadName);
            Assert.True(first.StatusCode == HttpStatusCode.OK, "the first preview of a sample failed");
            var firstConfirm = await ConfirmAsync(c, (await JsonOf(first)).GetProperty("batchId").GetGuid());
            Assert.True(firstConfirm.StatusCode == HttpStatusCode.OK, "the first confirm of a sample failed");
            var created = (await JsonOf(firstConfirm)).GetProperty("created").GetInt32();
            Assert.True(created == parsed.Lines.Count, "the first confirm did not create every line");

            // 1) Identical bytes: only the file-hash gate is exercised (AlreadyImported, confirm refused with 409).
            var same = await UploadAsync(c, account, bytes, uploadName);
            Assert.True(same.StatusCode == HttpStatusCode.OK, "the identical-bytes preview of a sample failed");
            var sameBody = await JsonOf(same);
            Assert.True(Codes(sameBody).Contains("AlreadyImported"), "identical bytes were not flagged as already imported");
            var sameConfirm = await ConfirmAsync(c, sameBody.GetProperty("batchId").GetGuid());
            Assert.True(sameConfirm.StatusCode == HttpStatusCode.Conflict, "identical bytes were not refused at confirm");
            Assert.True(await ErrorOf(sameConfirm) == "already_imported", "identical bytes were refused for another reason");

            // 2) Re-export (same content, different bytes): the hash gate does not apply, so every line must be found
            // by its (key, ordinal) identity and the confirm must create nothing.
            var reExport = LocalSamples.ReExport(bytes, isXlsx);
            Assert.True(!reExport.AsSpan().SequenceEqual(bytes), "the re-export has the same bytes");
            Assert.True(SameLines(parsed, LocalSamples.Read(reExport)), "the re-export does not read as the same lines");

            var again = await UploadAsync(c, account, reExport, uploadName);
            Assert.True(again.StatusCode == HttpStatusCode.OK, "the re-export preview of a sample failed");
            var againBody = await JsonOf(again);
            Assert.True(!Codes(againBody).Contains("AlreadyImported"), "the re-export was caught by the file hash");
            Assert.True(againBody.GetProperty("newCount").GetInt32() == 0, "the re-export preview has new lines");
            var lines = againBody.GetProperty("lines").EnumerateArray().ToList();
            Assert.True(lines.Count == parsed.Lines.Count, "the re-export preview has another line count");
            Assert.True(lines.All(l => l.GetProperty("existing").GetBoolean()), "a re-export line is not marked existing");

            var againConfirm = await ConfirmAsync(c, againBody.GetProperty("batchId").GetGuid());
            Assert.True(againConfirm.StatusCode == HttpStatusCode.OK, "the re-export confirm of a sample failed");
            Assert.True((await JsonOf(againConfirm)).GetProperty("created").GetInt32() == 0, "re-importing a re-export created lines");

            var stored = await CountTransactionsAsync(f, c);
            Assert.True(stored == parsed.Lines.Count, "re-importing a sample changed the stored lines");
        }
    }

    private static HashSet<string?> Codes(JsonElement preview) =>
        preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToHashSet();

    private static bool SameLines(ParsedStatement a, ParsedStatement b) =>
        a.Source == b.Source && a.Lines.SequenceEqual(b.Lines);

    private static async Task<int> CountTransactionsAsync(ApiFactory f, AuthedClient c)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        return await db.Transactions.AsNoTracking().CountAsync(t => !t.IsDeleted);
    }
}
