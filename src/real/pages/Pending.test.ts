import { beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { setApi } from "../api/context";
import { ApiError } from "../api/errors";
import type { ApiClient } from "../api";
import type { Category, Transaction } from "../api/types";
import { acc } from "../testing";
import { useTransactionsStore } from "../stores/transactions";
import { tx } from "../testingTx";
import Pending from "./Pending.vue";

const cats: Category[] = [{ id: "c1", name: "Mercado", kind: "Expense" } as Category];

async function setup(initial: Transaction[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  let pending = [...initial];
  const transactions = {
    list: vi.fn(async (q: { state?: string; size?: number; page?: number }) => ({
      items: q.size === 1 ? pending.slice(0, 1) : pending,
      page: q.page ?? 1,
      size: q.size ?? 50,
      total: pending.length,
    })),
    update: vi.fn(async (id: string, b: { state: Transaction["state"]; categoryId: string | null }) => {
      const cur = pending.find((p) => p.id === id)!;
      const saved = { ...cur, state: b.state, categoryId: b.categoryId };
      pending = pending.map((p) => (p.id === id ? saved : p)).filter((p) => p.state === "PendingClassification");
      return saved;
    }),
    create: vi.fn(),
    remove: vi.fn(),
  };
  setApi({
    transactions,
    accounts: { list: async () => [acc({ id: "a1", name: "Conta Ativa" })] },
    categories: { list: async () => cats },
  } as unknown as ApiClient);
  const w = mount(Pending, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, transactions };
}

const pend = (id: string, over: Partial<Transaction> = {}) =>
  tx({ id, state: "PendingClassification", categoryId: null, origin: "Imported", ...over });

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
});

describe("Pending page", () => {
  it("lists_only_pending_state_query", async () => {
    const { transactions } = await setup([pend("p1")]);
    const calls = transactions.list.mock.calls.map((c) => c[0] as { state?: string });
    expect(calls.some((c) => c.state === "PendingClassification")).toBe(true);
  });

  it("pending_inline_classify_updates_list_and_count", async () => {
    const { w, transactions } = await setup([pend("p1"), pend("p2", { description: "Outra" })]);
    const store = useTransactionsStore();
    expect(store.pendingCount).toBe(2);
    expect(w.findAll("[data-test=pending-row]")).toHaveLength(2);
    await w.findAll("[data-test=state-select]")[0].setValue("Normal");
    await flushPromises();
    const [id, body] = transactions.update.mock.calls[0] as unknown as [string, Record<string, unknown>];
    expect(id).toBe("p1");
    expect(body).toMatchObject({ state: "Normal", categoryId: null, amount: "-10.00", description: "Compra Exemplo", date: "2026-03-05" });
    expect(w.findAll("[data-test=pending-row]")).toHaveLength(1);
    expect(store.pendingCount).toBe(1);
  });

  it("empty_state_when_nothing_pending", async () => {
    const { w } = await setup([]);
    expect(w.find("[data-test=empty]").text()).toBe("Nenhum lançamento pendente");
  });

  it("rollback_on_api_error", async () => {
    const { w, transactions } = await setup([pend("p1")]);
    transactions.update.mockRejectedValueOnce(new ApiError(500, "x", undefined, "raw 9999.99"));
    await w.find("[data-test=state-select]").setValue("Normal");
    await flushPromises();
    expect(w.findAll("[data-test=pending-row]")).toHaveLength(1);
    expect((w.find("[data-test=state-select]").element as HTMLSelectElement).value).toBe("PendingClassification");
    const err = w.find("[data-test=error]");
    expect(err.text()).toContain("Algo deu errado");
    expect(err.text()).not.toContain("9999");
  });

  it("category_change_keeps_row_pending_and_sends_category", async () => {
    const { w, transactions } = await setup([pend("p1")]);
    await w.find("#pc-p1").setValue("c1");
    await flushPromises();
    expect((transactions.update.mock.calls[0] as unknown[])[1]).toMatchObject({ categoryId: "c1", state: "PendingClassification" });
    expect(w.findAll("[data-test=pending-row]")).toHaveLength(1);
  });
});
