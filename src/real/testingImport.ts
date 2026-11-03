import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { Account, Category, ConfirmResult, ImportBatch, ImportLine, ImportPreview } from "./api/types";
import { acc } from "./testing";

// Dados SINTÉTICOS para testes da importação (nada de arquivo real).

export function line(over: Partial<ImportLine> = {}): ImportLine {
  return {
    sourceKey: "k1",
    date: "2026-03-05",
    amount: "-10.00",
    description: "Compra Exemplo",
    state: "Normal",
    existing: false,
    ordinal: 0,
    installmentNumber: null,
    installmentTotal: null,
    cardSuffix: null,
    cardKind: null,
    purchaseDate: null,
    ...over,
  };
}

export function preview(over: Partial<ImportPreview> = {}): ImportPreview {
  const lines = over.lines ?? [
    line({ sourceKey: "n1", description: "Mercado Exemplo", amount: "-25.50" }),
    line({ sourceKey: "n2", description: "Salário Exemplo", amount: "1000.00" }),
    line({ sourceKey: "p1", description: "Transferência Exemplo", amount: "-40.00", state: "PendingClassification" }),
    line({ sourceKey: "e1", description: "Antiga Exemplo", amount: "-5.00", existing: true }),
  ];
  return {
    batchId: "b1",
    accountId: "a1",
    source: "NubankChecking",
    state: "Preview",
    periodStart: "2026-03-01",
    periodEnd: "2026-03-31",
    newCount: lines.filter((l) => !l.existing).length,
    existingCount: lines.filter((l) => l.existing).length,
    warnings: [],
    checkpoint: null,
    lines,
    createdAt: "2026-03-31T10:00:00Z",
    expiresAt: "2026-04-01T10:00:00Z",
    editedCount: 0,
    invoice: null,
    ...over,
  };
}

/** Prévia sintética de fatura de cartão do Itaú (linhas com parcela e final de cartão). */
export function invoicePreview(over: Partial<ImportPreview> = {}): ImportPreview {
  return preview({
    source: "ItauCard",
    invoice: { dueDate: "2026-04-10", declaredTotal: "150.00" },
    lines: [
      line({ sourceKey: "i1", description: "Loja Exemplo", amount: "-100.00", installmentNumber: 1, installmentTotal: 3, cardSuffix: "1234" }),
      line({ sourceKey: "i1", ordinal: 1, description: "Loja Exemplo", amount: "-50.00", cardSuffix: "1234" }),
    ],
    ...over,
  });
}

export function batch(over: Partial<ImportBatch> = {}): ImportBatch {
  return {
    batchId: "h1",
    accountId: "a1",
    source: "NubankChecking",
    state: "Confirmed",
    periodStart: "2026-02-01",
    periodEnd: "2026-02-28",
    newCount: 3,
    existingCount: 1,
    createdAt: "2026-03-01T10:00:00Z",
    expiresAt: null,
    invoice: null,
    ...over,
  };
}

export const importCats: Category[] = [
  { id: "c-exp", name: "Mercado", kind: "Expense" } as Category,
  { id: "c-inc", name: "Salário", kind: "Income" } as Category,
];

export function ofx(name = "extrato.ofx", content = "<OFX></OFX>"): File {
  return new File([content], name, { type: "application/x-ofx" });
}

export function xlsx(name = "fatura.xlsx", content = "PK"): File {
  return new File([content], name, { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });
}

export function fakeImportApi(opts: { accounts?: Account[]; preview?: ImportPreview; history?: ImportBatch[] } = {}) {
  const accounts = opts.accounts ?? [acc({ id: "a1", name: "Conta Exemplo", type: "Checking" })];
  const imports = {
    preview: vi.fn(async (_accountId: string, _file: File) => opts.preview ?? preview()),
    list: vi.fn(async () => opts.history ?? []),
    get: vi.fn(async (id: string) => preview({ batchId: id, state: "Confirmed", expiresAt: null })),
    confirm: vi.fn(async (id: string, _b: unknown): Promise<ConfirmResult> => ({ batchId: id, created: 3, skipped: 1, linked: 2, suggested: 1, categorized: 0 })),
    undo: vi.fn(async (_id: string) => undefined),
  };
  const transactions = {
    list: vi.fn(async () => ({ items: [], page: 1, size: 1, total: 7 })),
  };
  const api = {
    imports,
    transactions,
    accounts: { list: vi.fn(async () => accounts) },
    categories: { list: vi.fn(async () => importCats) },
  } as unknown as ApiClient;
  setApi(api);
  return { imports, transactions, accounts: (api as unknown as { accounts: { list: ReturnType<typeof vi.fn> } }).accounts };
}
