import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { Installments, Summary } from "./api/types";

export function sum(month: string, over: Partial<Summary> = {}): Summary {
  return {
    month,
    income: "1000.00",
    expenses: "-400.50",
    net: "599.50",
    byCategory: [
      { categoryId: "c1", categoryName: "Mercado", total: "-300.50" },
      { categoryId: null, categoryName: null, total: "-100.00" },
    ],
    pending: { count: 0, total: "0.00" },
    ...over,
  };
}

/** API falsa para a Visão geral; `summaryImpl` permite falhas/atrasos por mês. */
export function fakeOverviewApi(opts: {
  summaryImpl?: (month: string) => Promise<Summary>;
  accounts?: unknown[];
  recent?: unknown[];
  recentTotal?: number;
  pending?: number;
  installments?: Installments;
} = {}) {
  const recent = opts.recent ?? [];
  const summary = { month: vi.fn(opts.summaryImpl ?? (async (m: string) => sum(m))) };
  const accounts = { list: vi.fn(async () => opts.accounts ?? []) };
  const categories = { list: vi.fn(async () => []) };
  const transactions = {
    list: vi.fn(async (q: { state?: string; page?: number; size?: number }) =>
      q.state === "PendingClassification"
        ? { items: [], page: 1, size: 1, total: opts.pending ?? 0 }
        : { items: recent, page: 1, size: q.size ?? 5, total: opts.recentTotal ?? recent.length },
    ),
  };
  const installments = {
    get: vi.fn(async () => opts.installments ?? { totalCommitted: "0.00", accounts: [], months: [], groups: [] }),
  };
  setApi({ summary, accounts, categories, transactions, installments } as unknown as ApiClient);
  return { summary, accounts, categories, transactions, installments };
}
