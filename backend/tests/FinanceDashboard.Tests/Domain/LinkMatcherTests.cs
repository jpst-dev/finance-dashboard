using FinanceDashboard.Domain;

namespace FinanceDashboard.Tests.Domain;

public class LinkMatcherTests
{
    static readonly IReadOnlySet<(Guid OutId, Guid InId)> NoRejected = new HashSet<(Guid OutId, Guid InId)>();

    static readonly Dictionary<ImportSource, Guid> Accounts = new()
    {
        [ImportSource.NubankChecking] = Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
        [ImportSource.NubankCard] = Guid.Parse("00000000-0000-0000-0000-0000000000a2"),
        [ImportSource.ItauChecking] = Guid.Parse("00000000-0000-0000-0000-0000000000a3"),
        [ImportSource.ItauCard] = Guid.Parse("00000000-0000-0000-0000-0000000000a4"),
    };

    static int _n;
    static Guid NewId() => new(Interlocked.Increment(ref _n), 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    static PairingCandidate Out(ImportSource src, PairingRole role, int day, decimal amount = 100m, Guid? account = null) =>
        new(NewId(), account ?? Accounts[src], src, role, new DateOnly(2026, 3, day), -Math.Abs(amount));

    static PairingCandidate In(ImportSource src, PairingRole role, int day, decimal amount = 100m, Guid? account = null) =>
        new(NewId(), account ?? Accounts[src], src, role, new DateOnly(2026, 3, day), Math.Abs(amount));

    static PairingCandidate InvAcc(int day, decimal amount = 100m) =>
        Out(ImportSource.NubankChecking, PairingRole.InvoiceAccountSide, day, amount);
    static PairingCandidate InvCard(int day, decimal amount = 100m) =>
        In(ImportSource.NubankCard, PairingRole.InvoiceCardSide, day, amount);
    static PairingCandidate TrOut(int day, decimal amount = 100m) =>
        Out(ImportSource.NubankChecking, PairingRole.Transfer, day, amount);
    static PairingCandidate TrIn(int day, decimal amount = 100m) =>
        In(ImportSource.ItauChecking, PairingRole.Transfer, day, amount);

    static IReadOnlyList<ProposedLink> Match(params PairingCandidate[] c) => LinkMatcher.Match(c, NoRejected);

    [Fact]
    public void Single_exact_invoice_pair_is_auto_confirmed()
    {
        var o = InvAcc(10); var i = InvCard(11);
        var p = Assert.Single(Match(o, i));
        Assert.Equal(new ProposedLink(o.Id, i.Id, LinkKind.InvoicePayment, true), p);
    }

    [Fact]
    public void Invoice_pair_across_banks_does_not_match()
    {
        var o = Out(ImportSource.NubankChecking, PairingRole.InvoiceAccountSide, 10);
        var i = In(ImportSource.ItauCard, PairingRole.InvoiceCardSide, 10);
        Assert.Empty(Match(o, i));
    }

    [Fact]
    public void Amount_differs_by_one_cent_does_not_match()
    {
        Assert.Empty(Match(InvAcc(10, 100.00m), InvCard(10, 100.01m)));
        Assert.Empty(Match(InvAcc(10, 100.00m), InvCard(10, 99.99m)));
    }

    [Fact]
    public void Same_account_does_not_match()
    {
        var acc = Guid.NewGuid();
        var o = Out(ImportSource.NubankChecking, PairingRole.Transfer, 10, account: acc);
        var i = In(ImportSource.NubankChecking, PairingRole.Transfer, 10, account: acc);
        Assert.Empty(Match(o, i));
    }

    [Fact]
    public void Outside_window_does_not_match()
    {
        Assert.Empty(Match(InvAcc(10), InvCard(14)));
        Assert.Single(Match(InvAcc(10), InvCard(13)));
    }

    [Fact]
    public void Transfer_delta_1_is_auto_confirmed()
    {
        var p = Assert.Single(Match(TrOut(10), TrIn(11)));
        Assert.Equal(LinkKind.OwnTransfer, p.Kind);
        Assert.True(p.AutoConfirm);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Transfer_delta_2_and_3_are_suggested_not_auto(int delta)
    {
        var p = Assert.Single(Match(TrOut(10), TrIn(10 + delta)));
        Assert.Equal(LinkKind.OwnTransfer, p.Kind);
        Assert.False(p.AutoConfirm);
    }

    [Fact]
    public void Two_outs_one_in_suggests_both_pairs_none_auto()
    {
        var o1 = InvAcc(10); var o2 = InvAcc(11); var i = InvCard(11);
        var r = Match(o1, o2, i);
        Assert.Equal(2, r.Count);
        Assert.All(r, p => Assert.False(p.AutoConfirm));
        Assert.All(r, p => Assert.Equal(i.Id, p.InId));
        Assert.Equal(new[] { o1.Id, o2.Id }.OrderBy(x => x), r.Select(p => p.OutId).OrderBy(x => x));
    }

    [Fact]
    public void Two_by_two_identical_suggests_all_four()
    {
        var r = Match(InvAcc(10), InvAcc(10), InvCard(10), InvCard(10));
        Assert.Equal(4, r.Count);
        Assert.All(r, p => Assert.False(p.AutoConfirm));
        Assert.Equal(4, r.Select(p => (p.OutId, p.InId)).Distinct().Count());
    }

    [Fact]
    public void Rejected_pair_is_not_proposed_and_leaves_the_other_candidate_unambiguous()
    {
        var o1 = InvAcc(10); var o2 = InvAcc(10); var i = InvCard(10);
        var rejected = new HashSet<(Guid OutId, Guid InId)> { (o1.Id, i.Id) };
        var p = Assert.Single(LinkMatcher.Match(new[] { o1, o2, i }, rejected));
        Assert.Equal(o2.Id, p.OutId);
        Assert.Equal(i.Id, p.InId);
        Assert.True(p.AutoConfirm);

        Assert.Empty(LinkMatcher.Match(new[] { o1, i }, rejected));
    }

    [Fact]
    public void Transfer_and_invoice_roles_never_mix()
    {
        var o = Out(ImportSource.NubankChecking, PairingRole.InvoiceAccountSide, 10);
        var i = In(ImportSource.ItauChecking, PairingRole.Transfer, 10);
        Assert.Empty(Match(o, i));
        var o2 = Out(ImportSource.NubankChecking, PairingRole.Transfer, 10);
        var i2 = In(ImportSource.NubankCard, PairingRole.InvoiceCardSide, 10);
        Assert.Empty(Match(o2, i2));
    }

    [Fact]
    public void Manual_like_source_is_ignored_by_roles()
    {
        var o = TrOut(10);
        var i = In(ImportSource.NubankCard, PairingRole.Transfer, 10);
        Assert.Empty(Match(o, i));
    }

    [Fact]
    public void Order_of_input_does_not_change_output()
    {
        var c = new[] { InvAcc(10), InvAcc(11), InvCard(11), TrOut(5, 50m), TrIn(6, 50m), TrOut(20, 7m), TrIn(22, 7m) };
        var a = LinkMatcher.Match(c, NoRejected);
        var b = LinkMatcher.Match(c.Reverse().ToArray(), NoRejected);
        Assert.Equal(a, b);
    }

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 20).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Property_random_sets_are_deterministic_and_each_line_has_at_most_one_auto_pair(int seed)
    {
        var rng = new Random(seed);
        var sources = new[] { ImportSource.NubankChecking, ImportSource.NubankCard, ImportSource.ItauChecking, ImportSource.ItauCard };
        var list = new List<PairingCandidate>();
        int n = rng.Next(0, 13);
        for (int k = 0; k < n; k++)
        {
            var src = sources[rng.Next(sources.Length)];
            var role = src is ImportSource.NubankCard or ImportSource.ItauCard
                ? PairingRole.InvoiceCardSide
                : (rng.Next(2) == 0 ? PairingRole.InvoiceAccountSide : PairingRole.Transfer);
            int day = rng.Next(1, 8);
            decimal amt = rng.Next(1, 4) * 10m;
            list.Add(rng.Next(2) == 0 ? Out(src, role, day, amt) : In(src, role, day, amt));
        }
        var first = LinkMatcher.Match(list, NoRejected);
        var shuffled = list.OrderBy(_ => rng.Next()).ToList();
        Assert.Equal(first, LinkMatcher.Match(shuffled, NoRejected));

        var autos = first.Where(p => p.AutoConfirm).ToList();
        var lines = autos.SelectMany(p => new[] { p.OutId, p.InId }).ToList();
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }
}
