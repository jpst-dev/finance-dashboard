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

    static PairingCandidate Out(ImportSource src, PairingRole role, int day, decimal amount = 100m, Guid? account = null, string? name = null) =>
        new(NewId(), account ?? Accounts[src], src, role, new DateOnly(2026, 3, day), -Math.Abs(amount), name);

    static PairingCandidate In(ImportSource src, PairingRole role, int day, decimal amount = 100m, Guid? account = null, string? name = null) =>
        new(NewId(), account ?? Accounts[src], src, role, new DateOnly(2026, 3, day), Math.Abs(amount), name);

    const string Coherent = "JOSE LUIS DA SILVA";

    static PairingCandidate InvAcc(int day, decimal amount = 100m) =>
        Out(ImportSource.NubankChecking, PairingRole.InvoiceAccountSide, day, amount);
    static PairingCandidate InvCard(int day, decimal amount = 100m) =>
        In(ImportSource.NubankCard, PairingRole.InvoiceCardSide, day, amount);
    static PairingCandidate TrOut(int day, decimal amount = 100m) =>
        Out(ImportSource.NubankChecking, PairingRole.Transfer, day, amount, name: Coherent);
    static PairingCandidate TrIn(int day, decimal amount = 100m) =>
        In(ImportSource.ItauChecking, PairingRole.Transfer, day, amount, name: Coherent);
    static PairingCandidate PxCredit(int day, decimal amount = 100m, Guid? account = null) =>
        In(ImportSource.ItauChecking, PairingRole.PixCreditCredit, day, amount, account);
    static PairingCandidate PxDebit(int day, decimal amount = 100m, Guid? account = null) =>
        Out(ImportSource.ItauChecking, PairingRole.PixCreditDebit, day, amount, account);

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

    [Fact]
    public void PixCredit_same_account_single_pair_is_auto_confirmed()
    {
        var c = PxCredit(10); var d = PxDebit(10);
        var p = Assert.Single(Match(c, d));
        Assert.Equal(new ProposedLink(d.Id, c.Id, LinkKind.PixCredit, true), p);
    }

    [Fact]
    public void PixCredit_estorno_pairs_credit_with_EST_CRED_same_day()
    {
        var p = Assert.Single(Match(PxCredit(12, 55.50m), PxDebit(12, 55.50m)));
        Assert.Equal(LinkKind.PixCredit, p.Kind);
        Assert.True(p.AutoConfirm);
    }

    [Fact]
    public void PixCredit_different_accounts_do_not_match()
    {
        Assert.Empty(Match(PxCredit(10, account: Guid.NewGuid()), PxDebit(10, account: Guid.NewGuid())));
    }

    [Fact]
    public void PixCredit_outside_window_does_not_match()
    {
        Assert.Empty(Match(PxCredit(10), PxDebit(12)));
        Assert.Single(Match(PxCredit(10), PxDebit(11)));
    }

    [Fact]
    public void PixCredit_two_debits_one_credit_suggests_both()
    {
        var r = Match(PxCredit(10), PxDebit(10), PxDebit(11));
        Assert.Equal(2, r.Count);
        Assert.All(r, p => Assert.False(p.AutoConfirm));
        Assert.All(r, p => Assert.Equal(LinkKind.PixCredit, p.Kind));
    }

    [Fact]
    public void OwnTransfer_with_incoherent_names_is_suggested_not_auto()
    {
        var o = Out(ImportSource.ItauChecking, PairingRole.Transfer, 10, name: "ALUGUEL");
        var i = In(ImportSource.NubankChecking, PairingRole.Transfer, 10, name: "CLIENTE X");
        var p = Assert.Single(Match(o, i));
        Assert.Equal(LinkKind.OwnTransfer, p.Kind);
        Assert.False(p.AutoConfirm);
    }

    [Fact]
    public void OwnTransfer_with_coherent_names_is_auto_confirmed()
    {
        var o = Out(ImportSource.NubankChecking, PairingRole.Transfer, 10, name: "MARIA LUISA SANTOS");
        var i = In(ImportSource.ItauChecking, PairingRole.Transfer, 10, name: "MARIA L");
        Assert.True(Assert.Single(Match(o, i)).AutoConfirm);
    }

    [Fact]
    public void OwnTransfer_without_name_is_suggested()
    {
        var o = Out(ImportSource.NubankChecking, PairingRole.Transfer, 10);
        var i = In(ImportSource.ItauChecking, PairingRole.Transfer, 10);
        Assert.False(Assert.Single(Match(o, i)).AutoConfirm);
    }

    [Fact]
    public void OwnTransfer_name_shorter_than_7_is_not_coherent()
    {
        var o = Out(ImportSource.NubankChecking, PairingRole.Transfer, 10, name: "MARIA L");
        var i = In(ImportSource.ItauChecking, PairingRole.Transfer, 10, name: "MARIA");
        Assert.False(Assert.Single(Match(o, i)).AutoConfirm);
    }

    static IEnumerable<PairingCandidate> PixGroup(int outs, int ins, int outDay, int inDay, decimal amount = 100m) =>
        Enumerable.Range(0, outs).Select(_ => PxDebit(outDay, amount))
            .Concat(Enumerable.Range(0, ins).Select(_ => PxCredit(inDay, amount))).ToList();

    [Fact]
    public void PixCredit_three_identical_pairs_same_day_are_all_auto_confirmed()
    {
        var r = LinkMatcher.Match(PixGroup(3, 3, 10, 10).ToList(), NoRejected);
        Assert.Equal(3, r.Count);
        Assert.All(r, p => Assert.True(p.AutoConfirm));
        Assert.All(r, p => Assert.Equal(LinkKind.PixCredit, p.Kind));
        Assert.Equal(3, r.Select(p => p.OutId).Distinct().Count());
        Assert.Equal(3, r.Select(p => p.InId).Distinct().Count());
    }

    [Fact]
    public void PixCredit_two_credits_three_debits_identical_pairs_two_and_leaves_one()
    {
        var r = LinkMatcher.Match(PixGroup(3, 2, 10, 10).ToList(), NoRejected);
        Assert.Equal(2, r.Count);
        Assert.All(r, p => Assert.True(p.AutoConfirm));
        Assert.Equal(2, r.Select(p => p.OutId).Distinct().Count());
        Assert.Equal(2, r.Select(p => p.InId).Distinct().Count());
    }

    [Fact]
    public void PixCredit_same_day_different_amounts_stay_suggested()
    {
        var c = new[] { PxDebit(10), PxDebit(10, 150m), PxCredit(10), PxCredit(10, 150m) };
        // Two independent single-edge components (100/100 and 150/150): auto. Make amounts collide on one side instead:
        var r = LinkMatcher.Match(new[] { PxDebit(10), PxDebit(10), PxCredit(10), PxCredit(11) }, NoRejected);
        Assert.Equal(4, r.Count);
        Assert.All(r, p => Assert.False(p.AutoConfirm));
        Assert.Equal(2, LinkMatcher.Match(c, NoRejected).Count);
    }

    [Fact]
    public void PixCredit_interchangeable_group_with_neighbor_day_still_confirms_when_each_side_is_uniform()
    {
        var r = LinkMatcher.Match(PixGroup(3, 3, 10, 11).ToList(), NoRejected);
        Assert.Equal(3, r.Count);
        Assert.All(r, p => Assert.True(p.AutoConfirm));
    }

    [Fact]
    public void PixCredit_group_with_a_rejected_pair_falls_back_to_suggested()
    {
        var g = PixGroup(2, 2, 10, 10).ToList();
        var rejected = new HashSet<(Guid OutId, Guid InId)> { (g[0].Id, g[2].Id) };
        var r = LinkMatcher.Match(g, rejected);
        Assert.Equal(3, r.Count);
        Assert.All(r, p => Assert.False(p.AutoConfirm));
        Assert.DoesNotContain(r, p => p.OutId == g[0].Id && p.InId == g[2].Id);
    }

    [Fact]
    public void PixCredit_group_result_independent_of_input_order()
    {
        var g = PixGroup(3, 2, 10, 11).ToList();
        var a = LinkMatcher.Match(g, NoRejected);
        Assert.Equal(a, LinkMatcher.Match(Enumerable.Reverse(g).ToList(), NoRejected));
        Assert.Equal(a, LinkMatcher.Match(g.OrderBy(x => x.Id.GetHashCode() % 7).ThenBy(x => x.Date).ToList(), NoRejected));
        // The pairing is the Id-ordered zip.
        var outs = g.Where(x => x.Amount < 0).Select(x => x.Id).Order().ToList();
        var ins = g.Where(x => x.Amount > 0).Select(x => x.Id).Order().ToList();
        Assert.Equal(outs.Zip(ins).OrderBy(z => z.First).Select(z => (z.First, z.Second)),
            a.OrderBy(p => p.OutId).Select(p => (p.OutId, p.InId)));
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
                : (rng.Next(3) switch
                {
                    0 => PairingRole.InvoiceAccountSide,
                    1 => PairingRole.Transfer,
                    _ => src == ImportSource.ItauChecking ? PairingRole.PixCreditCredit : PairingRole.Transfer,
                });
            int day = rng.Next(1, 8);
            decimal amt = rng.Next(1, 4) * 10m;
            var nm = rng.Next(2) == 0 ? Coherent : null;
            bool isOut = rng.Next(2) == 0;
            if (role == PairingRole.PixCreditCredit && isOut) role = PairingRole.PixCreditDebit;
            list.Add(isOut ? Out(src, role, day, amt, name: nm) : In(src, role, day, amt, name: nm));
        }
        if (rng.Next(2) == 0)
            list.AddRange(PixGroup(rng.Next(1, 4), rng.Next(1, 4), rng.Next(1, 8), rng.Next(1, 8), 77m));
        var first = LinkMatcher.Match(list, NoRejected);
        var shuffled = list.OrderBy(_ => rng.Next()).ToList();
        Assert.Equal(first, LinkMatcher.Match(shuffled, NoRejected));

        var autos = first.Where(p => p.AutoConfirm).ToList();
        var lines = autos.SelectMany(p => new[] { p.OutId, p.InId }).ToList();
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }
}
