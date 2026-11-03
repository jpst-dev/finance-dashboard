import { beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { setApi } from "../api/context";
import { ApiError } from "../api/errors";
import type { ApiClient } from "../api";
import { acc } from "../testing";
import { oneSided } from "../testingLinks";
import { fakeTxApi, tx } from "../testingTx";
import Transactions from "./Transactions.vue";

async function setup() {
  const pinia = createPinia();
  setActivePinia(pinia);
  const transactions = {
    ...fakeTxApi([tx({ id: "t1", pairing: "OwnTransfer", description: "<b>x</b>" }), tx({ id: "t2", pairing: "InvoicePayment", state: "PendingClassification" })]),
    markOwn: vi.fn(async () => oneSided({ state: "Confirmed" })),
    markNoCounterpart: vi.fn(async () => oneSided({ state: "Confirmed", kind: "InvoicePayment" })),
  };
  const links = { list: vi.fn(async () => ({ items: [], page: 1, size: 50, total: 0 })) };
  setApi({ transactions, links, accounts: { list: async () => [acc({ id: "a1", name: "Conta" })] }, categories: { list: async () => [] } } as unknown as ApiClient);
  const w = mount(Transactions, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, transactions, links };
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
});

describe("Transactions page S3(b)", () => {
  it("own_and_no_counterpart_buttons_call_api_and_reload_links", async () => {
    const { w, transactions, links } = await setup();
    await w.find("[data-test=tx-own]").trigger("click");
    await flushPromises();
    expect(transactions.markOwn).toHaveBeenCalledWith("t1");
    expect(links.list).toHaveBeenCalled();
    await w.find("[data-test=tx-no-counterpart]").trigger("click");
    await flushPromises();
    expect(transactions.markNoCounterpart).toHaveBeenCalledWith("t2");
  });

  it("shows_error_for_422_and_description_as_text", async () => {
    const { w, transactions } = await setup();
    transactions.markOwn.mockRejectedValueOnce(new ApiError(422, "not_applicable"));
    await w.find("[data-test=tx-own]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=error]").text()).toContain("não se aplica");
    expect(w.find("[data-test=tx-desc] b").exists()).toBe(false);
  });
});
