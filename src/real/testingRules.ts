import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { CategoryRule } from "./api/types";
import { tx } from "./testingTx";

export function rule(over: Partial<CategoryRule> = {}): CategoryRule {
  return {
    id: "r1",
    pattern: "SUPERMERCADO EXEMPLO",
    categoryId: "c1",
    categoryName: "Alimentação",
    createdAt: "2026-03-06T10:00:00Z",
    matches: 3,
    ...over,
  };
}

/** Cliente falso com `categoryRules`, `transactions`, `categories` e `summary`. */
export function fakeRulesApi(rules: CategoryRule[] = []) {
  const state = { rules: [...rules] };
  const categoryRules = {
    list: vi.fn(async () => [...state.rules]),
    create: vi.fn(async (b: { pattern: string; categoryId: string }) => {
      const r = rule({ id: `r${state.rules.length + 2}`, pattern: b.pattern, categoryId: b.categoryId });
      state.rules.push(r);
      return r;
    }),
    remove: vi.fn(async (id: string) => {
      state.rules = state.rules.filter((r) => r.id !== id);
    }),
    apply: vi.fn(async () => ({ applied: 2 })),
  };
  const transactions = {
    list: vi.fn(async (q: { page?: number; size?: number }) => ({ items: [tx()], page: q.page ?? 1, size: q.size ?? 50, total: 1 })),
  };
  const categories = {
    list: vi.fn(async () => [
      { id: "c1", name: "Alimentação", kind: "Expense" },
      { id: "c2", name: "Salário", kind: "Income" },
    ]),
  };
  const summary = { month: vi.fn(async (m: string) => ({ month: m })) };
  setApi({ categoryRules, transactions, categories, summary } as unknown as ApiClient);
  return { categoryRules, transactions, categories, summary, state };
}
