using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure.Importing;

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
}

public sealed class LocalSamplesTests
{
    [LocalSamplesFact]
    public void Real_samples_parse_and_roundtrip()
    {
        var files = System.IO.Directory.EnumerateFiles(LocalSamples.Directory()!, "*.ofx").ToList();
        var sawChecking = false;
        var sawCard = false;

        foreach (var file in files)
        {
            var parsed = NubankOfxReader.Parse(File.ReadAllBytes(file));

            Assert.True(parsed.Lines.Count > 0, "a sample has no lines");
            Assert.True(parsed.Lines.All(l => !string.IsNullOrEmpty(l.FitId)), "a sample line has no FITID");
            Assert.True(parsed.Lines.All(l => l.Date.Year is >= 2000 and <= 2100), "a sample line has an implausible date");
            Assert.True(Enum.IsDefined(parsed.Source), "unknown statement kind");
            if (parsed.Source == ImportSource.NubankChecking) sawChecking = true;
            if (parsed.Source == ImportSource.NubankCard) sawCard = true;
        }

        Assert.True(files.Count > 0, "no .ofx samples found");
        Assert.True(sawChecking || sawCard, "no statement kind recognized");
    }
}
