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
    public void Real_base_newer_than_every_installment_line_projects_nothing_but_reports_basedOn()
    {
        var nov10 = new DateOnly(2026, 11, 10);
        var bases = new Dictionary<Guid, DateOnly> { [AccA] = nov10 };
        var p = InstallmentProjector.Project([Line(Oct10, 3, 4)], Today, bases);
        Assert.Equal(0m, p.TotalCommitted);
        Assert.Empty(p.Groups);
        Assert.Empty(p.Months);
        Assert.Equal(nov10, Assert.Single(p.Accounts).BasedOn);
    }

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
        Assert.Equal(3, p.Groups.Single().InstallmentNumber); // grupo guarda o N da linha-base
        Assert.Equal(Enumerable.Range(4, 7), p.Months.Select(m => m.Items.Single().InstallmentNumber));
        Assert.All(p.Months.SelectMany(m => m.Items), i => Assert.Equal(10, i.InstallmentTotal));
    }

    [Fact]
    public void Project_uses_AddMonths_from_base_without_drifting_day_31()
    {
        var p = InstallmentProjector.Project([Line(new DateOnly(2026, 1, 31), 1, 4)], new DateOnly(2026, 1, 5));
        // 3 parcelas restantes: 31/01 (2/4), 28/02 (3/4), 31/03 (4/4); nunca 28/03.
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)],
            p.Months.Select(m => m.Month));
        Assert.Equal([2, 3, 4], p.Months.Select(m => m.Items.Single().InstallmentNumber));
        Assert.Equal(new DateOnly(2026, 3, 31), p.Groups.Single().EndsOn);

        // Hoje em março: a parcela de fevereiro (28/02) fica no passado; a de março cai em 31/03.
        var q = InstallmentProjector.Project([Line(new DateOnly(2026, 1, 31), 1, 4)], new DateOnly(2026, 3, 1));
        Assert.Equal(new DateOnly(2026, 3, 1), q.Months.Single().Month);
        Assert.Equal(new DateOnly(2026, 3, 31), q.Groups.Single().EndsOn);
    }

    [Fact]
    public void Project_february_installment_falls_on_28_when_it_is_the_last()
    {
        var p = InstallmentProjector.Project([Line(new DateOnly(2026, 1, 31), 1, 3)], new DateOnly(2026, 1, 5));
        Assert.Equal(new DateOnly(2026, 2, 28), p.Groups.Single().EndsOn);
        Assert.Equal(new DateOnly(2026, 2, 1), p.Months[^1].Month);
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
        // 2/10 projeta 8 meses (out..mai) e 5/10 projeta 5 (out..fev): 200 nos 5 primeiros, depois só a cauda do 2/10.
        Assert.Equal([200m, 200m, 200m, 200m, 200m, 100m, 100m, 100m], p.Months.Select(m => m.Total));
        Assert.Equal([8, 9, 10], p.Months.Skip(5).Select(m => m.Items.Single().InstallmentNumber));
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
    public void Project_cut_boundary_current_month_counts_previous_month_does_not()
    {
        // base 10/09, 1/4: parcelas 10/09, 10/10, 10/11 => hoje 31/10 => out e nov; set fica de fora.
        var p = InstallmentProjector.Project([Line(new DateOnly(2026, 9, 10), 1, 4)], new DateOnly(2026, 10, 31));
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)], p.Months.Select(m => m.Month));
        Assert.Equal(2, p.Groups.Single().Remaining);
        // Parcela de 10/10 conta mesmo com hoje = 31/10 (mesmo mês); no dia 1/11 ela sai.
        var q = InstallmentProjector.Project([Line(new DateOnly(2026, 9, 10), 1, 4)], new DateOnly(2026, 11, 1));
        Assert.Equal([new DateOnly(2026, 11, 1)], q.Months.Select(m => m.Month));
    }

    [Fact]
    public void Project_fully_past_group_disappears_but_account_keeps_base()
    {
        var p = InstallmentProjector.Project([Line(new DateOnly(2026, 3, 10), 1, 3)], Today);
        Assert.Empty(p.Groups);
        Assert.Empty(p.Months);
        Assert.Equal(0m, p.TotalCommitted);
        Assert.Equal(new DateOnly(2026, 3, 10), p.Accounts.Single().BasedOn);
    }

    [Fact]
    public void Project_different_cards_are_different_purchases()
    {
        var p = InstallmentProjector.Project([Line(Oct10, 3, 5, card: "1111"), Line(Oct10, 3, 5, card: "2222")], Today);
        Assert.Equal(2, p.Groups.Count);
        Assert.All(p.Groups, g => Assert.Equal(1, g.Multiplicity));
        Assert.Equal(["1111", "2222"], p.Groups.Select(g => g.CardSuffix));
    }

    [Theory]
    [InlineData(6, 5)]  // N > T
    [InlineData(1, 1)]  // T < 2
    [InlineData(0, 5)]  // N < 1
    [InlineData(-1, 5)]
    public void Project_ignores_invalid_installment_lines(int n, int t)
    {
        var p = InstallmentProjector.Project([Line(Oct10, n, t)], Today);
        Assert.Empty(p.Accounts);
        Assert.Empty(p.Months);
        Assert.Empty(p.Groups);
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
            Line(Oct10, 1, 6, amount: -20m, desc: "ALFA", card: "9999"), Line(Oct10, 1, 6, amount: -20m, desc: "ALFA", card: "1111"), // cartões distintos = grupos distintos
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
