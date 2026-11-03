namespace FinanceDashboard.Domain;

public enum PairingRole { InvoiceAccountSide, InvoiceCardSide, Transfer, PixCreditCredit, PixCreditDebit }

public record PairingCandidate(Guid Id, Guid AccountId, ImportSource Source, PairingRole Role, DateOnly Date, decimal Amount, string? NameKey = null);

public record ProposedLink(Guid OutId, Guid InId, LinkKind Kind, bool AutoConfirm);

public static class LinkMatcher
{
    public const int WindowDays = 3;
    public const int AutoTransferDays = 1;
    public const int PixCreditWindowDays = 1;

    public static IReadOnlyList<ProposedLink> Match(
        IReadOnlyList<PairingCandidate> candidates,
        IReadOnlySet<(Guid OutId, Guid InId)> rejected)
    {
        var outs = candidates.Where(c => c.Amount < 0).OrderBy(c => c.Date).ThenBy(c => c.Id).ToList();
        var ins = candidates.Where(c => c.Amount > 0).OrderBy(c => c.Date).ThenBy(c => c.Id).ToList();

        var pairs = new List<(PairingCandidate Out, PairingCandidate In, LinkKind Kind)>();
        foreach (var o in outs)
            foreach (var i in ins)
            {
                if (o.Id == i.Id) continue;
                var isPix = o.Role == PairingRole.PixCreditDebit && i.Role == PairingRole.PixCreditCredit;
                if (isPix != (o.AccountId == i.AccountId)) continue;
                if (o.Amount != -i.Amount) continue;
                if (Math.Abs(o.Date.DayNumber - i.Date.DayNumber) > (isPix ? PixCreditWindowDays : WindowDays)) continue;
                if (rejected.Contains((o.Id, i.Id))) continue;
                var kind = KindOf(o, i);
                if (kind is null) continue;
                pairs.Add((o, i, kind.Value));
            }

        // Componentes conexos (união-busca sobre os ids das pontas).
        var parent = new Dictionary<Guid, Guid>();
        Guid Find(Guid x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        foreach (var p in pairs)
        {
            parent.TryAdd(p.Out.Id, p.Out.Id);
            parent.TryAdd(p.In.Id, p.In.Id);
            var a = Find(p.Out.Id); var b = Find(p.In.Id);
            if (a != b) parent[a] = b;
        }
        var edgesPerRoot = pairs.GroupBy(p => Find(p.Out.Id)).ToDictionary(g => g.Key, g => g.Count());

        return pairs
            .Select(p =>
            {
                var single = edgesPerRoot[Find(p.Out.Id)] == 1;
                var auto = single && !(p.Kind == LinkKind.OwnTransfer
                    && (Math.Abs(p.Out.Date.DayNumber - p.In.Date.DayNumber) > AutoTransferDays
                        || !NameKeys.Coincide(p.Out.NameKey, p.In.NameKey)));
                return (p, link: new ProposedLink(p.Out.Id, p.In.Id, p.Kind, auto));
            })
            .OrderBy(x => x.p.Out.Date).ThenBy(x => x.p.Out.Id).ThenBy(x => x.p.In.Id)
            .Select(x => x.link)
            .ToList();
    }

    static LinkKind? KindOf(PairingCandidate o, PairingCandidate i)
    {
        if (o.Role == PairingRole.PixCreditDebit && i.Role == PairingRole.PixCreditCredit)
            return o.Source == ImportSource.ItauChecking && i.Source == ImportSource.ItauChecking ? LinkKind.PixCredit : null;
        if (o.Role == PairingRole.Transfer && i.Role == PairingRole.Transfer)
            return IsChecking(o.Source) && IsChecking(i.Source) ? LinkKind.OwnTransfer : null;

        var sides = (o.Role, i.Role);
        var invoiceRoles = sides is (PairingRole.InvoiceAccountSide, PairingRole.InvoiceCardSide)
                                 or (PairingRole.InvoiceCardSide, PairingRole.InvoiceAccountSide);
        if (!invoiceRoles) return null;
        var acc = o.Role == PairingRole.InvoiceAccountSide ? o : i;
        var card = o.Role == PairingRole.InvoiceCardSide ? o : i;
        var sameBank = (acc.Source, card.Source) is (ImportSource.NubankChecking, ImportSource.NubankCard)
                                                 or (ImportSource.ItauChecking, ImportSource.ItauCard);
        return sameBank ? LinkKind.InvoicePayment : null;
    }

    static bool IsChecking(ImportSource s) => s is ImportSource.NubankChecking or ImportSource.ItauChecking;
}
