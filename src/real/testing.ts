import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { Account, Category } from "./api/types";

/** Cliente falso para testes: só `auth` é funcional; o resto lança se for usado. */
export function fakeApi(over: Partial<ApiClient["auth"]> = {}) {
  const auth = {
    me: vi.fn(async () => null as { email: string } | null),
    login: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
    ...over,
  };
  return { auth } as unknown as ApiClient & { auth: typeof auth };
}

export function acc(over: Partial<Account> = {}): Account {
  return {
    id: "a1",
    name: "Conta Exemplo",
    type: "Checking",
    institution: "Banco Exemplo",
    externalIdentifier: null,
    isArchived: false,
    balance: "100.00",
    ...over,
  };
}

export function fakeAccountsApi(list: Account[] = [acc()], cats: Category[] = []) {
  const accounts = {
    list: vi.fn(async () => list),
    create: vi.fn(async (b: { name: string | null }) => acc({ id: "new", name: b.name ?? "" })),
    update: vi.fn(
      async (
        id: string,
        b: { name: string | null; isArchived: boolean | null; type?: Account["type"] | null; institution?: string | null; externalIdentifier?: string | null },
      ) =>
        acc({
          id,
          name: b.name ?? "",
          isArchived: b.isArchived ?? false,
          ...(b.type ? { type: b.type } : {}),
          institution: b.institution ?? "",
          externalIdentifier: b.externalIdentifier ?? null,
        }),
    ),
    checkpoints: vi.fn(async () => []),
    addCheckpoint: vi.fn(async (_id: string, b: { date: string; amount: string }) => ({
      id: "c1",
      accountId: "a1",
      date: b.date,
      amount: b.amount,
      origin: "Manual" as const,
    })),
  };
  const categories = { list: vi.fn(async () => cats) };
  const api = { accounts, categories } as unknown as ApiClient;
  setApi(api);
  return { accounts, categories };
}

