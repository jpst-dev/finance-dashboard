using System.Security.Cryptography;
using System.Text;
using FinanceDashboard.Domain;
using FinanceDashboard.Infrastructure;
using FinanceDashboard.Infrastructure.Importing;
using FinanceDashboard.Infrastructure.Linking;
using FinanceDashboard.Tests.Infrastructure;
using FinanceDashboard.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceDashboard.Tests.DemoFixtures;

/// <summary>
/// Synthetic seed of the static demo: fixed ids, fixed dates and amounts from deterministic formulas (no random,
/// no real clock, no real data). Everything here is invented ("Exemplo"/"Fictício").
/// </summary>
public static class DemoSeed
{
    public const string UserId = "demo-visitor";
    public const string Email = "visitante@exemplo.invalid";

    /// <summary>Synthetic constant password of the generator's user; never written to the fixtures.</summary>
    internal const string Password = "Demo-Visitante-Sintetico-2026!";

    public static readonly DateOnly ReferenceDate = new(2026, 10, 15);

    public static readonly string[] Months = ["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"];

    private const int AccountKind = 0xA;
    private const int CategoryKindId = 0xC;
    private const int TransactionKind = 0x7;
    private const int CheckpointKind = 0xB;
    private const int BatchKind = 0x1B;
    private const int LinkKind = 0x1E;

    public static Guid Id(int kind, int n) => new(FormattableString.Invariant($"{kind:x8}-0000-4000-8000-{n:x12}"));

    public static Guid CheckingId => Id(AccountKind, 1);
    public static Guid CardId => Id(AccountKind, 2);
    public static Guid WalletId => Id(AccountKind, 3);

    /// <summary>Second checking account (another bank): the other end of the own-transfer pairs.</summary>
    public static Guid DigitalId => Id(AccountKind, 4);

    public static Guid ConfirmedBatchId => Id(BatchKind, 1);
    public static Guid PreviewBatchId => Id(BatchKind, 2);
    private static Guid ItauCheckingLinksBatchId => Id(BatchKind, 3);
    private static Guid ItauCardLinksBatchId => Id(BatchKind, 4);
    private static Guid DigitalLinksBatchId => Id(BatchKind, 5);

    /// <summary>Deterministic id for the n-th link in the generated fixtures (the server uses random ones).</summary>
    public static Guid FixtureLinkId(int n) => Id(LinkKind, n);

    // Same names and kinds as DefaultCategories, in its order: 10 expenses then 3 incomes.
    private static readonly (string Name, CategoryKind Kind)[] Categories =
    [
        ("Alimentação", CategoryKind.Expense), ("Moradia", CategoryKind.Expense), ("Transporte", CategoryKind.Expense),
        ("Saúde", CategoryKind.Expense), ("Lazer", CategoryKind.Expense), ("Educação", CategoryKind.Expense),
        ("Assinaturas", CategoryKind.Expense), ("Compras", CategoryKind.Expense), ("Impostos e taxas", CategoryKind.Expense),
        ("Outros", CategoryKind.Expense), ("Salário", CategoryKind.Income), ("Rendimentos", CategoryKind.Income),
        ("Outras receitas", CategoryKind.Income),
    ];

    private static Guid Cat(string name)
    {
        var i = Array.FindIndex(Categories, c => c.Name == name);
        return i < 0 ? throw new InvalidOperationException($"Unknown default category {name}.") : Id(CategoryKindId, i + 1);
    }

    /// <summary>Closing date of the confirmed October card invoice (imported 2026-10-05, due 2026-10-10).</summary>
    private static readonly DateOnly InvoiceClosing = new(2026, 10, 3);

    private static readonly DateTimeOffset CheckpointCreatedAt = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A monthly template: account, day, description, amount for month index i (0 = 2026-05), category.</summary>
    private sealed record Template(Guid Account, int Day, string Description, Func<int, decimal> Amount, string? Category);

    private static readonly Template[] Monthly =
    [
        new(CheckingId, 5, "Salário Empresa Exemplo", i => 5200.00m + i * 35.00m, "Salário"),
        new(CheckingId, 8, "Aluguel Apartamento Exemplo", _ => -1650.00m, "Moradia"),
        new(CheckingId, 12, "Conta de Luz Fictícia", i => -(142.37m + i * 7.35m), "Moradia"),
        new(CheckingId, 16, "Farmácia Exemplo", i => -(57.80m + i * 4.10m), "Saúde"),
        new(CheckingId, 20, "Mercado Exemplo", i => -(412.30m + i * 23.17m), "Alimentação"),
        new(CheckingId, 27, "Rendimento Poupança Exemplo", i => 12.40m + i * 0.83m, "Rendimentos"),
        new(CardId, 3, "Padaria Fictícia", i => -(38.90m + i * 2.15m), "Alimentação"),
        new(CardId, 14, "Streaming Exemplo", _ => -39.90m, "Assinaturas"),
        new(CardId, 18, "Livraria Fictícia", i => -(64.00m + i * 5.50m), "Educação"),
        new(CardId, 22, "Posto Combustível Exemplo", i => -(210.00m + i * 11.40m), "Transporte"),
        new(WalletId, 9, "Feira Livre Exemplo", i => -(64.50m + i * 3.25m), "Alimentação"),
        new(WalletId, 25, "Cinema Fictício", i => -(48.00m + i * 1.50m), "Lazer"),
    ];

    /// <summary>The preview's existing lines are the imported October checking lines with these keys.</summary>
    private static string CheckingKey(DateOnly date, string slug) =>
        FormattableString.Invariant($"demo-itau-cc-{date:yyyy-MM-dd}-{slug}");

    private static string CardKey(DateOnly date, string slug) =>
        FormattableString.Invariant($"demo-itau-cartao-{date:yyyy-MM-dd}-{slug}");

    private static string Hash(string label) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label)));

    public static async Task SeedAsync(ApiFactory f, CancellationToken ct = default)
    {
        using (var scope = f.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var result = await users.CreateAsync(new AppUser { Id = UserId, UserName = Email, Email = Email }, Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the demo user: " + string.Join("; ", result.Errors.Select(e => e.Code)));
            }
        }

        await using var db = PersistenceTestSupport.Create(f.ConnectionString, UserId);

        db.Accounts.AddRange(
            new Account { Id = CheckingId, UserId = UserId, Name = "Conta Corrente Exemplo", Type = AccountType.Checking, Institution = "Banco Exemplo" },
            new Account { Id = CardId, UserId = UserId, Name = "Cartão Exemplo", Type = AccountType.CreditCard, Institution = "Banco Exemplo" },
            new Account { Id = WalletId, UserId = UserId, Name = "Carteira", Type = AccountType.Wallet, Institution = "" },
            new Account { Id = DigitalId, UserId = UserId, Name = "Conta Digital Exemplo", Type = AccountType.Checking, Institution = "Banco Digital Exemplo" });

        for (var i = 0; i < Categories.Length; i++)
        {
            db.Categories.Add(new Category
            {
                Id = Id(CategoryKindId, i + 1), UserId = UserId, Name = Categories[i].Name, Kind = Categories[i].Kind, IsDefault = true,
            });
        }

        var checkingStart = 3500.00m;
        db.Checkpoints.AddRange(
            new BalanceCheckpoint
            {
                Id = Id(CheckpointKind, 1), UserId = UserId, AccountId = CheckingId, Date = new DateOnly(2026, 4, 30),
                Amount = checkingStart, Origin = CheckpointOrigin.Manual, CreatedAt = CheckpointCreatedAt,
            },
            new BalanceCheckpoint
            {
                Id = Id(CheckpointKind, 2), UserId = UserId, AccountId = WalletId, Date = new DateOnly(2026, 4, 30),
                Amount = 1200.00m, Origin = CheckpointOrigin.Manual, CreatedAt = CheckpointCreatedAt,
            },
            new BalanceCheckpoint
            {
                Id = Id(CheckpointKind, 3), UserId = UserId, AccountId = DigitalId, Date = new DateOnly(2026, 4, 30),
                Amount = 800.00m, Origin = CheckpointOrigin.Manual, CreatedAt = CheckpointCreatedAt,
            });

        var lines = new List<Transaction>();
        var n = 0;
        Transaction Add(Guid account, DateOnly date, decimal amount, string description, string? category,
            TransactionState state = TransactionState.Normal)
        {
            var t = new Transaction
            {
                Id = Id(TransactionKind, ++n), UserId = UserId, AccountId = account, Date = date, Amount = amount,
                OriginalDescription = description, NormalizedDescription = description,
                CategoryId = category is null ? null : Cat(category), State = state, Origin = TransactionOrigin.Manual,
            };
            lines.Add(t);
            return t;
        }

        var batchLines = new List<Transaction>();
        for (var i = 0; i < Months.Length; i++)
        {
            var year = 2026;
            var month = 5 + i;
            foreach (var tpl in Monthly)
            {
                var date = new DateOnly(year, month, tpl.Day);
                if (date > ReferenceDate) continue;
                var t = Add(tpl.Account, date, tpl.Amount(i), tpl.Description, tpl.Category);

                if (month == 10 && tpl.Account == CheckingId)
                {
                    // October checking lines came from an earlier Itaú checking import (synthetic keys).
                    t.Origin = TransactionOrigin.Imported;
                    t.SourceKey = CheckingKey(date, Slug(tpl.Description));
                    t.OriginalDescription = SourceKeys.NormalizeText(tpl.Description);
                }
                else if (month == 10 && tpl.Account == CardId && date <= InvoiceClosing)
                {
                    // Lines after the closing date belong to the next invoice (not imported yet).
                    batchLines.Add(t);
                }
            }
        }

        // One normal line without category.
        Add(CheckingId, new DateOnly(2026, 7, 15), -300.00m, "Transferência Fictícia", null);

        // A 3x installment purchase on the card (the last one is in the confirmed invoice).
        var purchase = new DateOnly(2026, 7, 28);
        for (var k = 1; k <= 3; k++)
        {
            var date = new DateOnly(2026, 7 + k, 2);
            var description = FormattableString.Invariant($"Loja Eletrônicos Exemplo {k}/3");
            var t = Add(CardId, date, -450.00m, description, "Compras");
            t.PurchaseDate = purchase;
            t.InstallmentNumber = k;
            t.InstallmentTotal = 3;
            t.Origin = TransactionOrigin.Imported;
            t.SourceKey = CardKey(date, FormattableString.Invariant($"loja-eletronicos-{k}-3"));
            t.OriginalDescription = SourceKeys.NormalizeText(description);
            if (k == 3) batchLines.Add(t);
        }

        // Pending classification: 1 in September, 2 in October.
        Add(CheckingId, new DateOnly(2026, 9, 17), 250.00m, "Pix Recebido Fictício", null, TransactionState.PendingClassification);
        Add(WalletId, new DateOnly(2026, 10, 11), -120.00m, "Despesa Sem Descrição Exemplo", null, TransactionState.PendingClassification);
        Add(CardId, new DateOnly(2026, 10, 13), -75.40m, "Compra Fictícia Não Identificada", null, TransactionState.PendingClassification);

        // Confirmed Itaú card invoice: the October card lines up to its closing date (all before CreatedAt and DueDate).
        foreach (var t in batchLines)
        {
            t.BatchId = ConfirmedBatchId;
            t.Origin = TransactionOrigin.Imported;
            t.CardSuffix = "0000";
            t.CardKind = CardKind.Physical;
            t.SourceKey ??= CardKey(t.Date, Slug(t.NormalizedDescription));
            if (t.InstallmentNumber is null) t.OriginalDescription = SourceKeys.NormalizeText(t.NormalizedDescription);
        }

        // One imported line edited by the user (description renamed).
        var edited = batchLines.First(t => t.InstallmentNumber is null);
        edited.NormalizedDescription += " (editado)";
        edited.EditedByUser = true;

        var confirmed = new ImportBatch
        {
            Id = ConfirmedBatchId, UserId = UserId, AccountId = CardId, Source = ImportSource.ItauCard,
            FileHash = Hash("demo-fatura-cartao-2026-10"), DueDate = new DateOnly(2026, 10, 10),
            // Itaú convention (ItauRules.InvoiceTotalMatches): declared total = −Σ(normal lines), positive.
            DeclaredTotal = -batchLines.Where(t => t.State == TransactionState.Normal).Sum(t => t.Amount),
            State = ImportBatchState.Confirmed, CreatedAt = new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero),
            NewCount = batchLines.Count, ExistingCount = 0,
        };

        var (linkBatches, ownPix, noCounterpart) = AddLinkCandidates(Add, confirmed.DeclaredTotal!.Value);

        // Preview of an Itaú checking statement: 3 lines already imported, 2 new normal, 1 new pending.
        var existing = lines.Where(t => t.AccountId == CheckingId && t.Origin == TransactionOrigin.Imported && t.BatchId is null)
            .OrderBy(t => t.Date).ToList();
        if (existing.Count != 3) throw new InvalidOperationException($"Expected 3 imported checking lines, got {existing.Count}.");

        var previewLines = existing
            .Select(t => new StoredPreviewLine(t.SourceKey!, t.Date, t.Amount, t.OriginalDescription, t.State, null, null, Existing: true))
            .ToList();
        var fresh = new (DateOnly Date, decimal Amount, string Memo, TransactionState State, string Slug)[]
        {
            (new DateOnly(2026, 10, 13), -85.00m, "PIX ENVIADO EXEMPLO", TransactionState.Normal, "pix-enviado"),
            (new DateOnly(2026, 10, 14), -29.90m, "TARIFA PACOTE FICTICIO", TransactionState.Normal, "tarifa"),
            (new DateOnly(2026, 10, 14), 400.00m, "TED RECEBIDA FICTICIA", TransactionState.PendingClassification, "ted-recebida"),
        };
        previewLines.AddRange(fresh.Select(l =>
            new StoredPreviewLine(CheckingKey(l.Date, l.Slug), l.Date, l.Amount, l.Memo, l.State, null, null, Existing: false)));

        var periodEnd = new DateOnly(2026, 10, 14);
        var ledger = checkingStart
            + lines.Where(t => t.AccountId == CheckingId && t.Date <= periodEnd).Sum(t => t.Amount)
            + fresh.Sum(l => l.Amount);

        var preview = new ImportBatch
        {
            Id = PreviewBatchId, UserId = UserId, AccountId = CheckingId, Source = ImportSource.ItauChecking,
            FileHash = Hash("demo-extrato-conta-2026-10"),
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = periodEnd,
            State = ImportBatchState.Preview,
            CreatedAt = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 10, 16, 11, 0, 0, TimeSpan.Zero),
            PreviewJson = new StoredPreview(null, new ImportCheckpointView(periodEnd, ledger), previewLines).Serialize(),
            WarningsJson = StoredPreview.SerializeWarnings([ImportWarning.PossibleDuplicate(1)]),
            NewCount = fresh.Length, ExistingCount = existing.Count,
        };

        db.ImportBatches.AddRange(confirmed, preview);
        db.ImportBatches.AddRange(linkBatches);
        db.Transactions.AddRange(lines);
        await db.SaveChangesAsync(ct);

        // The pairs come from the real matcher (LinkService), not hand-made rows.
        var reconciled = await new LinkService(db, f.Services.GetRequiredService<TimeProvider>()).ReconcileAsync(ct);
        if (reconciled.Linked != 3 || reconciled.Suggested != 1)
        {
            throw new InvalidOperationException($"Demo links drifted: {reconciled.Linked} linked, {reconciled.Suggested} suggested.");
        }

        // One-sided links come from the real marking actions: "é meu" (also learns the alias that then suggests the
        // other bank's line) and "sem contrapartida" on an invoice payment.
        var linkService = new LinkService(db, f.Services.GetRequiredService<TimeProvider>());
        if ((await linkService.MarkOwnAsync(ownPix, ct)).Outcome != LinkOutcome.Ok
            || (await linkService.MarkNoCounterpartAsync(noCounterpart, ct)).Outcome != LinkOutcome.Ok)
        {
            throw new InvalidOperationException("Demo one-sided links could not be created.");
        }

        var open = await db.Links.AsNoTracking().Where(l => l.OutTransactionId == null || l.InTransactionId == null).ToListAsync(ct);
        if (open.Count(l => l.State == LinkState.Confirmed) != 2 || open.Count(l => l.State == LinkState.Suggested) != 1)
        {
            throw new InvalidOperationException(
                "Demo one-sided links drifted: " + string.Join(", ", open.Select(l => $"{l.Kind}/{l.State}")) + ".");
        }

        var before = await db.Categories.CountAsync(ct);
        await DefaultCategories.EnsureForUserAsync(db, UserId, ct);
        var after = await db.Categories.CountAsync(ct);
        if (before != Categories.Length || after != before)
        {
            throw new InvalidOperationException($"Demo categories drifted from DefaultCategories ({before} → {after}).");
        }
    }

    /// <summary>
    /// Pending lines of three confirmed imports (Itaú checking, Itaú card, digital-bank checking) that the real pairing
    /// rules turn into: an invoice payment and an own transfer (both auto-confirmed) and an own transfer 2 days apart
    /// (suggested). Amounts are distinct so no line has two candidates.
    /// </summary>
    private static (ImportBatch[] Batches, Guid OwnPix, Guid NoCounterpart) AddLinkCandidates(
        Func<Guid, DateOnly, decimal, string, string?, TransactionState, Transaction> add, decimal invoiceTotal)
    {
        // State follows the real importers: Pix and boleto are Normal; invoice payment and PixCredit lines are Pendente.
        Transaction Line(Guid account, Guid batch, string keyPrefix, DateOnly date, decimal amount, string original,
            string description, string slug, string? counterparty = null, bool card = false,
            TransactionState state = TransactionState.PendingClassification)
        {
            var t = add(account, date, amount, original, null, state);
            t.NormalizedDescription = description;
            t.Origin = TransactionOrigin.Imported;
            t.BatchId = batch;
            t.SourceKey = FormattableString.Invariant($"{keyPrefix}-{date:yyyy-MM-dd}-{slug}");
            t.CounterpartyName = counterparty;
            if (card)
            {
                t.CardSuffix = "0000";
                t.CardKind = CardKind.Physical;
            }

            return t;
        }

        // Invoice payment: both sides inside the 3-day window, same amount.
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 10), -invoiceTotal,
            "FATURA PAGA CARTAO EXEMPLO", "Pagamento da fatura do cartão (exemplo)", "fatura-paga-cartao-exemplo");
        Line(CardId, ItauCardLinksBatchId, "demo-itau-cartao", new DateOnly(2026, 10, 11), invoiceTotal,
            "PAGAMENTO EFETUADO EXEMPLO", "Pagamento recebido (exemplo)", "pagamento-efetuado-exemplo", card: true);

        // Own transfer, same day, coherent names (PESSOA EXEMPLO on both sides): auto-confirmed.
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 12), -350.00m,
            "PIX TRANSF PESSOA EXEMPLO", "Pix para Conta Digital Exemplo", "pix-transf-pessoa-exemplo", state: TransactionState.Normal);
        Line(DigitalId, DigitalLinksBatchId, "demo-digital-cc", new DateOnly(2026, 10, 12), 350.00m,
            "Transferência recebida pelo Pix - PESSOA EXEMPLO - ***.000.000-** - BANCO EXEMPLO", "Pix recebido da Conta Corrente Exemplo", "pix-recebido-350", "Pessoa Exemplo", state: TransactionState.Normal);

        // Own transfer 2 days apart: only suggested.
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 6), -180.00m,
            "PIX TRANSF OUTRA EXEMPLO", "Pix para Conta Digital Exemplo (reserva)", "pix-transf-outra-exemplo", state: TransactionState.Normal);
        Line(DigitalId, DigitalLinksBatchId, "demo-digital-cc", new DateOnly(2026, 10, 8), 180.00m,
            "Transferência recebida pelo Pix - PESSOA EXEMPLO - ***.000.000-** - BANCO EXEMPLO", "Pix recebido da Conta Corrente Exemplo (reserva)", "pix-recebido-180", "Pessoa Exemplo", state: TransactionState.Normal);

        // PixCredit pair (loan credit and its card-limit debit): same account, same day and amount, auto-confirmed.
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 7), 640.00m,
            "CREDITO LIBERAD PIX 1234", "Crédito liberado via Pix (exemplo)", "credito-liberad-pix-1234");
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 7), -640.00m,
            "PIX CARTAO Exemplo20 08", "Pix no crédito (exemplo)", "pix-cartao-exemplo20-08");

        // Own Pix marked "é meu" (one-sided Confirmed, learns the alias CONTA RESERVA) ...
        var ownPix = Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 9), -220.00m,
            "PIX TRANSF CONTA RESERVA09 10", "Pix para Conta Reserva (exemplo)", "pix-transf-conta-reserva", state: TransactionState.Normal);
        // ... and, in the other bank, a line whose name starts with that alias: one-sided Suggested.
        Line(DigitalId, DigitalLinksBatchId, "demo-digital-cc", new DateOnly(2026, 10, 9), 95.00m,
            "Transferência recebida pelo Pix - CONTA RESERVA EXEMPLO - ***.000.000-** - BANCO EXEMPLO",
            "Pix recebido de Conta Reserva Exemplo", "pix-recebido-95", "Conta Reserva Exemplo", state: TransactionState.Normal);

        // Invoice payment whose card side is not in the system: marked "sem contrapartida".
        var noCounterpart = Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 2), -1180.25m,
            "FATURA PAGA CARTAO EXTERNO EXEMPLO", "Pagamento de fatura de outro cartão (exemplo)", "fatura-paga-cartao-externo-exemplo");

        // A boleto and a Pix to a third party: Normal and never linked.
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 4), -512.30m,
            "PAG BOLETO CONDOMINIO EXEMPLO", "Boleto Condomínio Exemplo", "pag-boleto-condominio-exemplo", state: TransactionState.Normal);
        Line(CheckingId, ItauCheckingLinksBatchId, "demo-itau-cc", new DateOnly(2026, 10, 11), -64.90m,
            "PIX TRANSF FORNECEDOR FICTICIO11 10", "Pix para Fornecedor Fictício", "pix-transf-fornecedor-ficticio", state: TransactionState.Normal);

        ImportBatch Batch(Guid id, Guid account, ImportSource source, string label, DateOnly end, int count) => new()
        {
            Id = id, UserId = UserId, AccountId = account, Source = source, FileHash = Hash(label),
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = end, State = ImportBatchState.Confirmed,
            CreatedAt = new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), NewCount = count, ExistingCount = 0,
        };

        return (
        [
            Batch(ItauCheckingLinksBatchId, CheckingId, ImportSource.ItauChecking, "demo-extrato-conta-vinculos-2026-10", new DateOnly(2026, 10, 12), 9),
            Batch(ItauCardLinksBatchId, CardId, ImportSource.ItauCard, "demo-fatura-cartao-vinculos-2026-10", new DateOnly(2026, 10, 11), 1),
            Batch(DigitalLinksBatchId, DigitalId, ImportSource.NubankChecking, "demo-extrato-digital-2026-10", new DateOnly(2026, 10, 12), 3),
        ], ownPix.Id, noCounterpart.Id);
    }

    private static string Slug(string description) =>
        SourceKeys.NormalizeText(description).ToLowerInvariant().Replace(' ', '-');
}
