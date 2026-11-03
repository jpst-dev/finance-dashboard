using System.Globalization;
using System.Net;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using FinanceDashboard.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FinanceDashboard.Tests.Importing.ImportTestApi;

namespace FinanceDashboard.Tests.Importing;

/// <summary>
/// Property-style: random sequences of preview / confirm / undo / reimport over 2–3 overlapping synthetic files.
/// A model tracks which file created each key; after every step the database must hold exactly the model's keys,
/// never twice, and reimporting a confirmed file must never change the total.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ImportIdempotencyTests(PostgresFixture postgres)
{
    private const int Seed = 20261102;
    private const int Runs = 25;

    private sealed record SyntheticFile(byte[] Bytes, IReadOnlyList<string> Keys);

    [Fact]
    public async Task Random_sequences_of_preview_confirm_undo_reimport_never_duplicate_lines()
    {
        var rng = new Random(Seed);
        await using var f = new ApiFactory(await postgres.CreateDatabaseAsync());
        // One user (the login rate limiter caps logins); each run has its own account and its own random FITIDs.
        using var c = await AuthedClient.CreateAsync(f, "a@example.com", Password);
        var undos = 0;
        var reimportsOfConfirmed = 0;

        for (var run = 0; run < Runs; run++)
        {
            var acc = await CreateAccountAsync(c);
            var files = BuildFiles(rng);

            var open = new Dictionary<int, Guid>();
            var confirmed = new Dictionary<int, Guid>();
            var owner = new Dictionary<string, int>(StringComparer.Ordinal);
            var steps = rng.Next(6, 14);

            for (var step = 0; step < steps; step++)
            {
                var file = rng.Next(files.Count);
                var context = $"run {run}, step {step}";
                switch (rng.Next(4))
                {
                    case 0:
                        open[file] = await PreviewAsync(c, acc, files[file].Bytes);
                        break;

                    case 1 when open.Remove(file, out var batch):
                        await ConfirmAndCheckAsync(c, batch, file, files, confirmed, owner, context);
                        break;

                    case 1:
                        open[file] = await PreviewAsync(c, acc, files[file].Bytes);
                        break;

                    case 2 when confirmed.Remove(file, out var batch):
                        undos++;
                        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batch)).StatusCode);
                        foreach (var key in owner.Where(o => o.Value == file).Select(o => o.Key).ToList()) owner.Remove(key);
                        break;

                    case 2 when open.Remove(file, out var batch):
                        Assert.Equal(HttpStatusCode.NoContent, (await UndoAsync(c, batch)).StatusCode);
                        break;

                    case 2:
                        break;

                    default:
                    {
                        var before = await CountAsync(f, c, acc);
                        open.Remove(file); // the new upload replaces any open preview of the same file
                        var batch = await PreviewAsync(c, acc, files[file].Bytes);
                        var wasConfirmed = confirmed.ContainsKey(file);
                        if (wasConfirmed) reimportsOfConfirmed++;
                        await ConfirmAndCheckAsync(c, batch, file, files, confirmed, owner, context);
                        if (wasConfirmed) Assert.True(before == await CountAsync(f, c, acc), context);
                        break;
                    }
                }

                await AssertMatchesModelAsync(f, c, acc, owner, context);
            }
        }

        // The fixed seed must actually exercise the interesting transitions.
        Assert.True(undos > 0, "no undo of a confirmed batch was generated");
        Assert.True(reimportsOfConfirmed > 0, "no reimport of a confirmed file was generated");
    }

    private static async Task ConfirmAndCheckAsync(AuthedClient c, Guid batch, int file, IReadOnlyList<SyntheticFile> files,
        Dictionary<int, Guid> confirmed, Dictionary<string, int> owner, string context)
    {
        var r = await ConfirmAsync(c, batch);
        if (confirmed.ContainsKey(file))
        {
            Assert.True(r.StatusCode == HttpStatusCode.Conflict, context);
            Assert.True(await ErrorOf(r) == "already_imported", context);
            return;
        }

        Assert.True(r.StatusCode == HttpStatusCode.OK, context);
        var fresh = files[file].Keys.Where(k => !owner.ContainsKey(k)).ToList();
        foreach (var key in fresh) owner[key] = file;
        confirmed[file] = batch;
        var body = await JsonOf(r);
        Assert.True(fresh.Count == body.GetProperty("created").GetInt32(), context);
        Assert.True(files[file].Keys.Count - fresh.Count == body.GetProperty("skipped").GetInt32(), context);
    }

    private static async Task<int> CountAsync(ApiFactory f, AuthedClient c, Guid acc)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        return await db.Transactions.CountAsync(t => t.AccountId == acc);
    }

    private static async Task AssertMatchesModelAsync(
        ApiFactory f, AuthedClient c, Guid acc, Dictionary<string, int> owner, string context)
    {
        await using var db = PersistenceTestSupport.Create(f.ConnectionString, c.UserId);
        var keys = await db.Transactions.Where(t => t.AccountId == acc).Select(t => t.SourceKey!).ToListAsync();
        Assert.True(keys.Count == keys.Distinct(StringComparer.Ordinal).Count(), context + ": duplicated (account, key)");
        Assert.True(owner.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(keys), context + ": keys differ from the model");
    }

    /// <summary>2–3 files over a pool of 10 keys; consecutive windows overlap. Same key ⇒ same line in every file.</summary>
    private static List<SyntheticFile> BuildFiles(Random rng)
    {
        var pool = Enumerable.Range(0, 10).Select(i =>
        {
            var bytes = new byte[16];
            rng.NextBytes(bytes);
            var key = new Guid(bytes).ToString();
            var day = 1 + rng.Next(28);
            var cents = (rng.Next(1, 100_000) * (rng.Next(2) == 0 ? -1 : 1)) / 100m;
            return (Key: key, Posted: $"202610{day:00}000000[-3:BRT]",
                Amount: cents.ToString("0.00", CultureInfo.InvariantCulture), Memo: $"Compra no debito - Loja {i}");
        }).ToList();

        var count = rng.Next(2, 4);
        var files = new List<SyntheticFile>(count);
        for (var i = 0; i < count; i++)
        {
            var start = i * 3 + rng.Next(0, 2);
            var length = rng.Next(4, 7);
            var lines = pool.Skip(start).Take(length).OrderBy(_ => rng.Next()).ToList();
            var builder = OfxTestFile.Checking().NoLedger();
            foreach (var l in lines) builder.Line(l.Posted, l.Amount, l.Memo, l.Key);
            files.Add(new SyntheticFile(builder.Build(), lines.Select(l => l.Key).ToList()));
        }

        return files;
    }
}
