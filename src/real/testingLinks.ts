import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { LinkResponse } from "./api/types";
import { tx } from "./testingTx";

export function link(over: Partial<LinkResponse> = {}): LinkResponse {
  return {
    id: "l1",
    kind: "OwnTransfer",
    state: "Suggested",
    origin: "Auto",
    out: { transactionId: "t-out", accountId: "a1", date: "2026-03-05", description: "Transferência enviada", amount: "-100.00" },
    in: { transactionId: "t-in", accountId: "a2", date: "2026-03-06", description: "Transferência recebida", amount: "100.00" },
    createdAt: "2026-03-06T10:00:00Z",
    ...over,
  };
}

/** Cliente falso com `links` e `transactions` (a loja de vínculos recarrega as pendências). */
export function fakeLinksApi(links: LinkResponse[] = []) {
  const state = { links: [...links] };
  const linksApi = {
    list: vi.fn(async (q: { state?: string; page?: number; size?: number }) => {
      const items = state.links.filter((l) => !q.state || l.state === q.state);
      return { items, page: q.page ?? 1, size: q.size ?? 50, total: items.length };
    }),
    confirm: vi.fn(async (id: string) => {
      const l = state.links.find((x) => x.id === id)!;
      l.state = "Confirmed";
      return l;
    }),
    reject: vi.fn(async (id: string) => {
      const l = state.links.find((x) => x.id === id)!;
      l.state = "Rejected";
      return l;
    }),
    undo: vi.fn(async (id: string) => {
      state.links = state.links.filter((x) => x.id !== id);
    }),
    reconcile: vi.fn(async () => ({ linked: 0, suggested: 0 })),
  };
  const transactions = {
    list: vi.fn(async (q: { page?: number; size?: number }) => ({ items: [tx()], page: q.page ?? 1, size: q.size ?? 50, total: 1 })),
  };
  setApi({ links: linksApi, transactions } as unknown as ApiClient);
  return { links: linksApi, transactions, state };
}
