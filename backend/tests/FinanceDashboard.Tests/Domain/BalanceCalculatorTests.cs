using FinanceDashboard.Domain;

namespace FinanceDashboard.Tests.Domain;

public class BalanceCalculatorTests
{
    private static BalanceCheckpoint Cp(string date, decimal amount, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "u",
        AccountId = Guid.NewGuid(),
        Date = DateOnly.Parse(date),
        Amount = amount,
        Origin = CheckpointOrigin.Manual,
        CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch,
    };

    private static LedgerLine L(string date, decimal amount) => new(DateOnly.Parse(date), amount);

    [Fact]
    public void No_checkpoint_sums_all_lines()
    {
        var result = BalanceCalculator.Compute(null, [L("2026-10-01", 10m), L("2026-10-02", -3.5m)]);
        Assert.Equal(6.5m, result);
    }

    [Fact]
    public void Lines_on_or_before_checkpoint_date_are_excluded()
    {
        var cp = Cp("2026-10-31", 100m);
        var result = BalanceCalculator.Compute(cp, [L("2026-10-30", 7m), L("2026-10-31", 50m), L("2026-11-01", -20m)]);
        Assert.Equal(80m, result);
    }

    [Fact]
    public void Latest_is_greatest_date_then_most_recently_created()
    {
        var t0 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var old = Cp("2026-10-01", 1m, t0.AddDays(5));
        var a = Cp("2026-10-31", 2m, t0);
        var b = Cp("2026-10-31", 3m, t0.AddHours(1));

        Assert.Same(b, BalanceCalculator.Latest([old, a, b]));
        Assert.Same(b, BalanceCalculator.Latest([b, a, old]));
        Assert.Null(BalanceCalculator.Latest([]));
    }

    [Fact]
    public void Empty_is_zero()
    {
        Assert.Equal(0m, BalanceCalculator.Compute(null, []));
    }
}
