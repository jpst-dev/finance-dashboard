import { beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { setApi } from "../api/context";
import { ApiError } from "../api/errors";
import type { ApiClient } from "../api";
import type { Transaction } from "../api/types";
import { acc } from "../testing";
import { oneSided } from "../testingLinks";
import { tx } from "../testingTx";
import Pending from "./Pending.vue";

async function setup(initial: Transaction[], reconcile = { linked: 2, suggested: 1, reclassified: 0 }) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const transactions = {
    list: vi.fn(async (q: { size?: number; page?: number }) => ({
      items: q.size === 1 ? initial.slice(0, 1) : initial,
      page: q.page ?? 1,
      size: q.size ?? 50,
      total: initial.length,
    })),
    markOwn: vi.fn(async () => oneSided({ state: "Confirmed" })),
    markNoCounterpart: vi.fn(async () => oneSided({ state: "Confirmed", kind: "InvoicePayment" })),
  };
  const links = {
    list: vi.fn(async (q: { state?: string }) => ({
      items: q.state === "Suggested" ? [oneSided()] : [],
      page: 1,
      size: 50,
      total: q.state === "Suggested" ? 1 : 0,
    })),
    reconcile: vi.fn(async () => reconcile),
  };
  setApi({
    transactions,
    accounts: { list: async () => [acc({ id: "a1", name: "Conta Ativa" })] },
    categories: { list: async () => [] },
    links,
  } as unknown as ApiClient);
  const w = mount(Pending, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, transactions, links };
}

const pend = (id: string, over: Partial<Transaction> = {}) =>
  tx({ id, state: "PendingClassification", categoryId: null, origin: "Imported", ...over });

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
});

describe("Pending page S3(b)", () => {
  it("reconcile_button_calls_api_shows_summary_and_reloads", async () => {
    const { w, links, transactions } = await setup([pend("p1")]);
    const lists = links.list.mock.calls.length;
    const txLists = transactions.list.mock.calls.length;
    const btn = w.find("[data-test=reconcile]");
    expect(btn.text()).toBe("Reconciliar pendências");
    await btn.trigger("click");
    await flushPromises();
    expect(links.reconcile).toHaveBeenCalledTimes(1);
    expect(w.find("[data-test=reconcile-result]").text()).toBe("2 ligadas, 1 sugestão, nenhuma reclassificada");
    expect(links.list.mock.calls.length).toBeGreaterThan(lists);
    expect(transactions.list.mock.calls.length).toBeGreaterThan(txLists);
  });

  it("reconcile_button_is_disabled_while_running", async () => {
    const { w, links } = await setup([pend("p1")]);
    let release!: (v: { linked: number; suggested: number; reclassified: number }) => void;
    links.reconcile.mockImplementationOnce(() => new Promise((r) => (release = r)));
    await w.find("[data-test=reconcile]").trigger("click");
    await flushPromises();
    expect((w.find("[data-test=reconcile]").element as HTMLButtonElement).disabled).toBe(true);
    release({ linked: 0, suggested: 0, reclassified: 0 });
    await flushPromises();
    expect((w.find("[data-test=reconcile]").element as HTMLButtonElement).disabled).toBe(false);
  });

  it("reconcile_error_is_shown", async () => {
    const { w, links } = await setup([pend("p1")]);
    links.reconcile.mockRejectedValueOnce(new ApiError(0));
    await w.find("[data-test=reconcile]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=error]").exists()).toBe(true);
    expect(w.find("[data-test=reconcile-result]").exists()).toBe(false);
  });

  it("pending_row_actions_follow_pairing_and_call_api", async () => {
    const { w, transactions } = await setup([
      pend("p1", { pairing: "OwnTransfer" }),
      pend("p2", { pairing: "InvoicePayment" }),
      pend("p3", { pairing: null }),
    ]);
    const rows = w.findAll("[data-test=pending-row]");
    expect(rows.map((r) => r.find("[data-test=tx-own]").exists())).toEqual([true, false, false]);
    expect(rows.map((r) => r.find("[data-test=tx-no-counterpart]").exists())).toEqual([false, true, false]);
    await rows[0].find("[data-test=tx-own]").trigger("click");
    await flushPromises();
    expect(transactions.markOwn).toHaveBeenCalledWith("p1");
    await w.findAll("[data-test=pending-row]")[1].find("[data-test=tx-no-counterpart]").trigger("click");
    await flushPromises();
    expect(transactions.markNoCounterpart).toHaveBeenCalledWith("p2");
  });

  it("mark_conflict_shows_error_text", async () => {
    const { w, transactions } = await setup([pend("p1", { pairing: "OwnTransfer" })]);
    transactions.markOwn.mockRejectedValueOnce(new ApiError(409));
    await w.find("[data-test=tx-own]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=error]").text()).toContain("vínculo mudou");
  });

  it("renders_third_party_text_as_text", async () => {
    const { w } = await setup([pend("p1", { description: "<b>x</b>", pairing: "OwnTransfer" })]);
    expect(w.find("[data-test=pending-row] b").exists()).toBe(false);
    expect(w.find("[data-test=tx-desc]").text()).toBe("<b>x</b>");
  });
});
