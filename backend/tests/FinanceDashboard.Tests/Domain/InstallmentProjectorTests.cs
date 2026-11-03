using FinanceDashboard.Domain;

namespace FinanceDashboard.Tests.Domain;

public class InstallmentProjectorTests
{
    private static readonly Guid AccA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid AccB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static InstallmentLine Line(DateOnly statement, int n, int t, decimal amount = -100m, string desc = "LOJA EXEMPLO",
        Guid? account = null, string accountName = "Cartao A", string? card = "1234") =>
        new(account ?? AccA, accountName, card, desc, desc, n, t, amount, statement);

    private static readonly DateOnly Oct10 = new(2026, 10, 10);
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public void Project_three_of_ten_gives_seven_remaining_in_the_month_the_line_would_fall()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 3, 10)], Today);
        Assert.Equal(7, p.Months.Sum(m => m.Items.Count));
        Assert.Equal(new DateOnly(2026, 10, 1), p.Months[0].Month);
        Assert.Equal(new DateOnly(2027, 4, 1), p.Months[^1].Month);
        Assert.All(p.Months.SelectMany(m => m.Items), i => Assert.Equal(100m, i.Amount));
        Assert.Equal(700m, p.TotalCommitted);
        Assert.Equal(new DateOnly(2027, 4, 10), p.Groups.Single().EndsOn);
        Assert.Equal(7, p.Groups.Single().Remaining);
    }

    [Fact]
    public void Project_uses_AddMonths_from_base_without_drifting_day_31()
    {
        var p = InstallmentProjector.Project([Line(new DateOnly(2026, 1, 31), 1, 4)], new DateOnly(2026, 1, 5));
        Assert.Equal(3, p.Months.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), p.Months[0].Month);
        // 3 parcelas restantes: 31/01, 28/02, 31/03 (nunca 28/03).
        Assert.Equal(new DateOnly(2026, 3, 31), p.Groups.Single().EndsOn);
    }

    [Fact]
    public void Project_ignores_completed_group_N_equals_T()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 10, 10)], Today);
        Assert.Empty(p.Months);
        Assert.Empty(p.Groups);
        Assert.Equal(0m, p.TotalCommitted);
    }

    [Fact]
    public void Project_only_latest_invoice_lines_count()
    {
        var older = Line(new DateOnly(2026, 9, 10), 2, 6, desc: "ANTIGA");
        var latest = Line(Oct10, 3, 5, desc: "ATUAL");
        var p = InstallmentProjector.Project([older, latest], Today);
        Assert.Equal("ATUAL", p.Groups.Single().Description);
        Assert.Equal(2, p.Months.Sum(m => m.Items.Count));
        Assert.Equal(Oct10, p.Accounts.Single().BasedOn);
    }

    [Fact]
    public void Project_cuts_installments_before_today_month()
    {
        // base 10/05, 1/10: parcelas em mai..jan; hoje é outubro => só out, nov, dez, jan.
        var p = InstallmentProjector.Project([Line(new DateOnly(2026, 5, 10), 1, 10)], Today);
        // i=1..9 => datas 10/05 ... 10/01/2027; >= 01/10: out, nov, dez, jan.
        Assert.Equal(4, p.Months.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), p.Months[0].Month);
        Assert.Equal(400m, p.TotalCommitted);
        Assert.Equal(4, p.Groups.Single().Remaining);
        Assert.Equal(new DateOnly(2026, 5, 10), p.Accounts.Single().BasedOn);
    }

    [Fact]
    public void Project_same_store_different_stages_projects_each_line()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 2, 10), Line(Oct10, 5, 10)], Today);
        Assert.Equal(13, p.Months.Sum(m => m.Items.Count));
        Assert.Equal(2, p.Groups.Count);
        Assert.Equal([5, 8], p.Groups.Select(g => g.Remaining).OrderBy(x => x));
    }

    [Fact]
    public void Project_identical_lines_get_multiplicity()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 3, 5), Line(Oct10, 3, 5)], Today);
        var g = Assert.Single(p.Groups);
        Assert.Equal(2, g.Multiplicity);
        Assert.Equal(2, g.Remaining);
        Assert.Equal(400m, g.RemainingTotal);
        Assert.All(p.Months, m => Assert.Equal(200m, m.Total));
        Assert.Equal(400m, p.TotalCommitted);
    }

    [Fact]
    public void Project_separate_accounts_have_separate_bases()
    {
        var a = Line(Oct10, 1, 3, account: AccA, accountName: "Cartao A");
        var b = Line(new DateOnly(2026, 11, 5), 1, 3, account: AccB, accountName: "Cartao B");
        var p = InstallmentProjector.Project([a, b], Today);
        Assert.Equal(2, p.Accounts.Count);
        Assert.Equal(Oct10, p.Accounts.Single(x => x.AccountId == AccA).BasedOn);
        Assert.Equal(new DateOnly(2026, 11, 5), p.Accounts.Single(x => x.AccountId == AccB).BasedOn);
        Assert.Equal(2, p.Groups.Count);
        Assert.Equal(4, p.Months.Sum(m => m.Items.Count));
    }

    [Fact]
    public void Project_ignores_non_negative_amounts()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 1, 5, amount: 100m), Line(Oct10, 1, 5, amount: 0m)], Today);
        Assert.Empty(p.Accounts);
        Assert.Empty(p.Months);
        Assert.Empty(p.Groups);
    }

    [Fact]
    public void Project_horizon_is_36_installments()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 1, 48, amount: -10m)], Today);
        Assert.Equal(36, p.Months.Sum(m => m.Items.Count));
        Assert.Equal(360m, p.TotalCommitted);
        Assert.Equal(36, p.Groups.Single().Remaining);
        Assert.Equal(new DateOnly(2029, 9, 10), p.Groups.Single().EndsOn);
    }

    [Fact]
    public void Project_groups_remaining_total_and_total_committed_agree()
    {
        var lines = new[]
        {
            Line(Oct10, 3, 10), Line(Oct10, 3, 10), Line(Oct10, 1, 4, amount: -33.33m, desc: "OUTRA"),
            Line(Oct10, 9, 12, amount: -50m, desc: "TERCEIRA", card: null),
        };
        var p = InstallmentProjector.Project(lines, Today);
        Assert.Equal(p.Months.Sum(m => m.Total), p.TotalCommitted);
        Assert.Equal(p.TotalCommitted, p.Groups.Sum(g => g.RemainingTotal));
        Assert.Equal(1400m + 99.99m + 150m, p.TotalCommitted);
    }

    [Fact]
    public void Project_empty_input_gives_empty_projection()
    {
        var p = InstallmentProjector.Project([], Today);
        Assert.Equal(0m, p.TotalCommitted);
        Assert.Empty(p.Accounts);
        Assert.Empty(p.Months);
        Assert.Empty(p.Groups);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void Property_projection_is_independent_of_input_order(int seed)
    {
        var lines = new List<InstallmentLine>
        {
            Line(Oct10, 3, 10), Line(Oct10, 3, 10), Line(Oct10, 2, 10, desc: "ZETA"), Line(Oct10, 5, 10, desc: "ZETA"),
            Line(Oct10, 1, 6, amount: -20m, desc: "ALFA", card: "9999"), Line(Oct10, 1, 6, amount: -20m, desc: "ALFA", card: "1111"),
            Line(new DateOnly(2026, 9, 10), 4, 8, desc: "ANTIGA"),
            Line(new DateOnly(2026, 11, 3), 1, 3, account: AccB, accountName: "Cartao B", desc: "BETA"),
        };
        var expected = InstallmentProjector.Project(lines, Today);
        var rng = new Random(seed);
        var shuffled = lines.OrderBy(_ => rng.Next()).ToList();
        var actual = InstallmentProjector.Project(shuffled, Today);

        Assert.Equal(expected.TotalCommitted, actual.TotalCommitted);
        Assert.Equal(expected.Accounts, actual.Accounts);
        Assert.Equal(expected.Groups, actual.Groups);
        Assert.Equal(expected.Months.Count, actual.Months.Count);
        for (var i = 0; i < expected.Months.Count; i++)
        {
            Assert.Equal(expected.Months[i].Month, actual.Months[i].Month);
            Assert.Equal(expected.Months[i].Total, actual.Months[i].Total);
            Assert.Equal(expected.Months[i].Items, actual.Months[i].Items);
        }
    }
}
