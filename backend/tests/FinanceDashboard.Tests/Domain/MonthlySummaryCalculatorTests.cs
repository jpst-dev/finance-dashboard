using FinanceDashboard.Domain;

namespace FinanceDashboard.Tests.Domain;

public class MonthlySummaryCalculatorTests
{
    private static SummaryLine S(decimal amount, AccountType type = AccountType.Checking,
        TransactionState state = TransactionState.Normal, Guid? cat = null) => new(amount, type, state, cat);

    [Fact]
    public void Income_counts_positive_normal_lines_on_non_card_accounts()
    {
        var r = MonthlySummaryCalculator.Compute([
            S(100m), S(50m, AccountType.Wallet), S(30m, AccountType.CreditCard),
        ]);
        Assert.Equal(150m, r.Income);
    }

    [Fact]
    public void Expenses_sum_negative_normal_lines_on_any_account()
    {
        var r = MonthlySummaryCalculator.Compute([
            S(-40m), S(-10m, AccountType.CreditCard), S(-5m, AccountType.Wallet),
        ]);
        Assert.Equal(-55m, r.Expenses);
    }

    [Fact]
    public void Card_refund_positive_reduces_expenses_and_is_not_income()
    {
        var r = MonthlySummaryCalculator.Compute([S(-100m, AccountType.CreditCard), S(30m, AccountType.CreditCard)]);
        Assert.Equal(0m, r.Income);
        Assert.Equal(-70m, r.Expenses);
    }

    [Fact]
    public void Pending_lines_are_excluded_and_reported_with_count_and_total()
    {
        var r = MonthlySummaryCalculator.Compute([
            S(100m), S(-20m),
            S(-300m, state: TransactionState.PendingClassification),
            S(50m, state: TransactionState.PendingClassification),
        ]);
        Assert.Equal(100m, r.Income);
        Assert.Equal(-20m, r.Expenses);
        Assert.Equal(2, r.PendingCount);
        Assert.Equal(-250m, r.PendingTotal);
    }

    [Fact]
    public void Net_is_income_plus_expenses()
    {
        var r = MonthlySummaryCalculator.Compute([S(100m), S(-35m)]);
        Assert.Equal(65m, r.Net);
    }

    [Fact]
    public void By_category_groups_signed_totals_with_null_for_uncategorized()
    {
        var food = Guid.NewGuid();
        var r = MonthlySummaryCalculator.Compute([
            S(-10m, cat: food), S(-5m, cat: food), S(-7m), S(20m), S(1m, cat: food),
        ]);
        Assert.Equal(2, r.ByCategory.Count);
        Assert.Equal(-14m, r.ByCategory.Single(c => c.CategoryId == food).Total);
        Assert.Equal(13m, r.ByCategory.Single(c => c.CategoryId is null).Total);
    }

    [Fact]
    public void Month_with_only_pending_returns_zeros_and_pending_count()
    {
        var r = MonthlySummaryCalculator.Compute([S(-10m, state: TransactionState.PendingClassification)]);
        Assert.Equal(0m, r.Income);
        Assert.Equal(0m, r.Expenses);
        Assert.Equal(0m, r.Net);
        Assert.Empty(r.ByCategory);
        Assert.Equal(1, r.PendingCount);
        Assert.Equal(-10m, r.PendingTotal);
    }
}
