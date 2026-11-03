import { beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { ApiError } from "../api/errors";
import { acc } from "../testing";
import { tx } from "../testingTx";
import { fakeOverviewApi, sum } from "../testingSummary";
import { useValuesVisibility } from "../money/visibility";
import Overview from "./Overview.vue";

vi.mock("vue-chartjs", async () => {
  const { defineComponent, h } = await import("vue");
  const make = (name: string) =>
    defineComponent({
      name,
      props: ["data", "options"],
      setup(_, { expose }) {
        expose({ chart: { update() {} } });
        return () => h("canvas");
      },
    });
  return { Bar: make("bar"), Doughnut: make("doughnut") };
});

const RouterLink = {
  props: ["to"],
  template: `<a :href="typeof to === 'string' ? to : to.path"><slot /></a>`,
};

async function setup(opts: Parameters<typeof fakeOverviewApi>[0] = {}) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const api = fakeOverviewApi({ recent: [tx()], ...opts });
  const w = mount(Overview, { global: { plugins: [pinia, i18n], stubs: { RouterLink } } });
  await flushPromises();
  return { w, api };
}

beforeEach(() => {
  localStorage.clear();
  i18n.global.locale.value = "pt-BR";
});

describe("Overview", () => {
  it("Pending_notice_hidden_when_zero_and_links_when_positive", async () => {
    const zero = await setup({ pending: 0 });
    expect(zero.w.find("[data-test=pending-notice]").exists()).toBe(false);
    const some = await setup({ pending: 3 });
    const n = some.w.find("[data-test=pending-notice]");
    expect(n.text()).toContain("3 lançamentos pendentes");
    expect(n.find("a").attributes("href")).toBe("/pending");
    const one = await setup({ pending: 1 });
    expect(one.w.find("[data-test=pending-notice]").text()).toContain("1 lançamento pendente");
  });

  it("Cards_show_server_values_and_no_percent_change", async () => {
    const { w } = await setup({
      summaryImpl: async (m) =>
        sum(m, { income: "1234.50", expenses: "-200.25", net: "1034.25", pending: { count: 2, total: "-50.00" } }),
    });
    const cards = w.find("[data-test=summary-cards]");
    expect(cards.text()).toMatch(/1\.234,50/);
    expect(cards.text()).toMatch(/200,25/);
    expect(cards.text()).toMatch(/1\.034,25/);
    expect(cards.text()).not.toContain("%");
    const p = w.find("[data-test=month-pending]");
    expect(p.text()).toContain("2 pendentes fora do resumo");
    expect(p.text()).toMatch(/50,00/);
    useValuesVisibility().toggle();
    await flushPromises();
    for (const c of ["card-income", "card-expenses", "card-net"]) expect(w.find(`[data-test=${c}]`).text()).not.toMatch(/[0-9]/);
    expect(w.find("[data-test=month-pending]").text()).toContain("2 pendentes");
    expect(w.find("[data-test=month-pending]").text()).not.toMatch(/50,00/);
  });

  it("Archived_accounts_not_in_strip", async () => {
    const { w } = await setup({
      accounts: [
        acc({ id: "a1", name: "Conta Ativa", balance: "10.00" }),
        acc({ id: "a2", name: "Conta Velha", isArchived: true }),
        acc({ id: "a3", name: "Cartão X", type: "CreditCard", balance: "-99.90" }),
      ],
    });
    const strip = w.find("[data-test=account-strip]");
    expect(strip.text()).toContain("Conta Ativa");
    expect(strip.text()).not.toContain("Conta Velha");
    expect(strip.text()).toContain("Dívida");
    expect(strip.find("a[href='/accounts']").exists()).toBe(true);
  });

  it("Empty_state_links_to_import", async () => {
    const { w } = await setup({ recent: [], recentTotal: 0 });
    const e = w.find("[data-test=empty-state]");
    expect(e.exists()).toBe(true);
    expect(e.find("a").attributes("href")).toBe("/import");
  });

  it("not_empty_when_transactions_exist_and_recent_links_to_all", async () => {
    const { w } = await setup({ recent: [tx({ description: "Padaria <b>X</b>" })], recentTotal: 9 });
    expect(w.find("[data-test=empty-state]").exists()).toBe(false);
    const r = w.find("[data-test=recent]");
    expect(r.text()).toContain("Padaria <b>X</b>"); // texto, não HTML
    expect(r.find("b").exists()).toBe(false);
    expect(r.find("a[href='/transactions']").exists()).toBe(true);
  });

  it("month_picker_navigates_and_does_not_pass_current_month", async () => {
    const { w, api } = await setup();
    expect(w.find("[data-test=month-next]").attributes("disabled")).toBeDefined();
    await w.find("[data-test=month-prev]").trigger("click");
    await flushPromises();
    expect(api.summary.month).toHaveBeenCalledTimes(12);
    expect(w.find("[data-test=month-next]").attributes("disabled")).toBeUndefined();
  });

  it("block_errors_are_translated_and_retry_works", async () => {
    let fail = true;
    const { w, api } = await setup({
      summaryImpl: async (m) => {
        if (fail) throw new ApiError(500, "boom-9999.99");
        return sum(m);
      },
    });
    const err = w.find("[data-test=summary-error]");
    expect(err.text()).toContain("Algo deu errado");
    expect(w.html()).not.toContain("9999.99");
    fail = false;
    await w.find("[data-test=summary-retry]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=summary-error]").exists()).toBe(false);
    expect(api.summary.month).toHaveBeenCalledTimes(12);
  });

  it("locale_and_hidden_toggles_do_not_refetch", async () => {
    const { w, api } = await setup();
    const calls = api.summary.month.mock.calls.length;
    useValuesVisibility().toggle();
    i18n.global.locale.value = "en-US";
    await flushPromises();
    expect(api.summary.month.mock.calls.length).toBe(calls);
    expect(w.find("h1").text()).toBe("Overview");
  });

  it("Document_title_has_no_values", async () => {
    document.title = "Visão geral · Finance Dashboard";
    await setup();
    expect(document.title).not.toMatch(/[0-9]/);
  });
});
