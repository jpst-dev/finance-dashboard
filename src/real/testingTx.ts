import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { Transaction } from "./api/types";

export function tx(over: Partial<Transaction> = {}): Transaction {
  return {
    id: "t1",
    accountId: "a1",
    date: "2026-03-05",
    purchaseDate: null,
    statementDate: null,
    amount: "-10.00",
    description: "Compra Exemplo",
    originalDescription: "Compra Exemplo",
    categoryId: "c1",
    state: "Normal",
    origin: "Manual",
    editedByUser: false,
    counterpartyName: null,
    ...over,
  };
}

export function fakeTxApi(items: Transaction[] = [tx()], total = items.length) {
  const transactions = {
    list: vi.fn(async (q: { page?: number; size?: number }) => ({ items, page: q.page ?? 1, size: q.size ?? 50, total })),
    create: vi.fn(async () => tx({ id: "new" })),
    update: vi.fn(async (id: string) => tx({ id })),
    remove: vi.fn(async () => undefined),
  };
  setApi({ transactions } as unknown as ApiClient);
  return transactions;
}

