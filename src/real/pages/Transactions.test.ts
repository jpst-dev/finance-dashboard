import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import i18n from "../i18n";
import { setApi } from "../api/context";
import type { ApiClient } from "../api";
import type { Category } from "../api/types";
import { useValuesVisibility } from "../money/visibility";
import { acc } from "../testing";
import { fakeTxApi, tx } from "../testingTx";
import Transactions from "./Transactions.vue";
import TransactionForm from "../components/TransactionForm.vue";

const cats: Category[] = [
  { id: "c1", name: "Mercado", kind: "Expense" } as Category,
  { id: "c2", name: "Salário", kind: "Income" } as Category,
];

async function setup(opts: { items?: ReturnType<typeof tx>[]; total?: number } = {}) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const transactions = fakeTxApi(opts.items ?? [tx()], opts.total);
  const accounts = {
    list: async () => [acc({ id: "a1", name: "Conta Ativa" }), acc({ id: "a2", name: "Conta Velha", isArchived: true, type: "CreditCard" }), acc({ id: "a3", name: "Cartão Ativo", type: "CreditCard" })],
  };
  setApi({ transactions, accounts, categories: { list: async () => cats } } as unknown as ApiClient);
  const w = mount(Transactions, { global: { plugins: [pinia, i18n, createRouter({ history: createMemoryHistory(), routes: [] })] } });
  await flushPromises();
  return { w, transactions };
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
});

describe("Transactions page", () => {
  it("amounts_render_with_sign_and_mask_when_hidden", async () => {
    const { w } = await setup({ items: [tx({ amount: "-1234.56" }), tx({ id: "t2", amount: "50.00" })] });
    const cells = w.findAll("[data-test=tx-amount]");
    expect(cells[0].text()).toContain("−");
    expect(cells[0].text()).toMatch(/1\.234,56/);
    expect(cells[1].text()).toContain("+");
    useValuesVisibility().toggle();
    await flushPromises();
    expect(w.html()).not.toMatch(/1\.234|1234\.56|50,00/);
    expect(w.find("[data-test=tx-amount]").text()).toContain("••••");
  });

  it("dates_render_ddmmyyyy_from_text", async () => {
    const { w } = await setup({ items: [tx({ date: "2026-03-01" })] });
    expect(w.find("[data-test=tx-row] td").text()).toBe("01/03/2026");
  });

  it("description_with_markup_renders_as_text", async () => {
    const { w } = await setup({ items: [tx({ description: "<img src=x onerror=alert(1)>" })] });
    expect(w.find("[data-test=tx-desc]").text()).toBe("<img src=x onerror=alert(1)>");
    expect(w.find("[data-test=tx-desc] img").exists()).toBe(false);
  });

  it("archived_accounts_not_offered_in_form_but_present_in_filter", async () => {
    const { w } = await setup();
    const filterOpts = w.findAll("#f-account option").map((o) => o.text());
    expect(filterOpts.some((o) => o.includes("Conta Velha"))).toBe(true);
    await w.find("[data-test=new-tx]").trigger("click");
    const formOpts = w.findAll("#tx-account option").map((o) => o.text());
    expect(formOpts).toEqual(["Conta Ativa", "Cartão Ativo"]);
  });

  it("create_rejects_comma_thousand_formats_before_sending", async () => {
    const { w, transactions } = await setup();
    await w.find("[data-test=new-tx]").trigger("click");
    await w.find("#tx-amount").setValue("12,345");
    await w.find("#tx-description").setValue("Padaria");
    await w.find("[data-test=tx-form]").trigger("submit");
    expect(transactions.create).not.toHaveBeenCalled();
    expect(w.find("[data-test=form-error]").text()).toContain("Valor inválido");
    await w.find("#tx-amount").setValue("1.234,50");
    await w.find("[data-test=tx-form]").trigger("submit");
    await flushPromises();
    expect(transactions.create).toHaveBeenCalledTimes(1);
    const body = (transactions.create.mock.calls[0] as unknown[])[0] as Record<string, unknown>;
    expect(body).toMatchObject({ accountId: "a1", amount: "-1234.50", description: "Padaria", categoryId: null });
    expect(body.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it("income_toggle_and_typed_minus_produce_api_signed_string", async () => {
    const { w, transactions } = await setup();
    await w.find("[data-test=new-tx]").trigger("click");
    await w.find("[data-test=kind-income]").setValue(true);
    await w.find("#tx-amount").setValue("20");
    await w.find("#tx-description").setValue("Venda");
    await w.find("[data-test=tx-form]").trigger("submit");
    await flushPromises();
    expect((transactions.create.mock.calls[0] as unknown[])[0]).toMatchObject({ amount: "20" });
    await w.find("[data-test=new-tx]").trigger("click");
    await w.find("[data-test=kind-income]").setValue(true);
    await w.find("#tx-amount").setValue("-3,5");
    await w.find("#tx-description").setValue("Troco");
    await w.find("[data-test=tx-form]").trigger("submit");
    await flushPromises();
    expect((transactions.create.mock.calls[1] as unknown[])[0]).toMatchObject({ amount: "-3.50" });
  });

  it("row_actions_have_row_context_in_aria_label", async () => {
    const { w } = await setup({ items: [tx({ description: "Padaria Exemplo" })] });
    expect(w.find("[data-test=tx-edit]").attributes("aria-label")).toBe("Editar: Padaria Exemplo");
    expect(w.find("[data-test=tx-delete]").attributes("aria-label")).toBe("Apagar: Padaria Exemplo");
  });

  it("edit_mode_account_label_does_not_point_at_a_paragraph", async () => {
    const { w } = await setup();
    await w.find("[data-test=tx-edit]").trigger("click");
    expect(w.find("label[for=tx-account]").exists()).toBe(false);
    expect(w.find("[data-test=tx-account-fixed]").attributes("aria-labelledby")).toBe("tx-account-label");
  });

  it("form_account_follows_accounts_loaded_after_open", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    setApi({ categories: { list: async () => cats } } as unknown as ApiClient);
    const form = mount(TransactionForm, { props: { accounts: [], busy: false }, global: { plugins: [pinia, i18n] } });
    await form.setProps({ accounts: [acc({ id: "a9", name: "Conta Tardia" })] });
    await form.find("#tx-description").setValue("Algo");
    await form.find("#tx-amount").setValue("5");
    await form.find("[data-test=tx-form]").trigger("submit");
    expect(form.emitted("submit")![0][0]).toMatchObject({ accountId: "a9" });
  });

  it("credit_card_hint_is_shown", async () => {
    const { w } = await setup();
    await w.find("[data-test=new-tx]").trigger("click");
    expect(w.find("[data-test=card-hint]").exists()).toBe(false);
    await w.find("#tx-account").setValue("a3");
    expect(w.find("[data-test=card-hint]").text()).toContain("negativo");
  });

  it("imported_delete_copy_mentions_hidden", async () => {
    const { w, transactions } = await setup({ items: [tx({ origin: "Imported" })] });
    await w.find("[data-test=tx-delete]").trigger("click");
    expect(w.find("[data-test=confirm-delete]").text()).toContain("oculto");
    expect(transactions.remove).not.toHaveBeenCalled();
    await w.find("[data-test=confirm-yes]").trigger("click");
    await flushPromises();
    expect(transactions.remove).toHaveBeenCalledWith("t1");
  });

  it("manual_delete_copy_says_removed", async () => {
    const { w } = await setup();
    await w.find("[data-test=tx-delete]").trigger("click");
    const text = w.find("[data-test=confirm-delete]").text();
    expect(text).toContain("removido");
    expect(text).not.toContain("oculto");
  });

  it("edit_clearing_category_sends_null", async () => {
    const { w, transactions } = await setup({ items: [tx({ categoryId: "c1" })] });
    await w.find("[data-test=tx-edit]").trigger("click");
    await w.find("#tx-category").setValue("");
    await w.find("[data-test=tx-form]").trigger("submit");
    await flushPromises();
    const [id, body] = transactions.update.mock.calls[0] as unknown as [string, Record<string, unknown>];
    expect(id).toBe("t1");
    expect(body.categoryId).toBeNull();
    expect(body.amount).toBe("-10.00");
    expect(body.state).toBe("Normal");
  });

  it("pagination_buttons_disabled_at_edges_and_page_of_pages", async () => {
    const { w, transactions } = await setup({ total: 120 });
    expect(w.find("[data-test=page-info]").text()).toBe("Página 1 de 3");
    expect(w.find("[data-test=prev]").attributes("disabled")).toBeDefined();
    await w.find("[data-test=next]").trigger("click");
    await flushPromises();
    expect((transactions.list.mock.calls.at(-1)![0] as { page: number }).page).toBe(2);
  });

  it("page_beyond_range_shows_empty_state_not_error", async () => {
    const { w } = await setup({ items: [], total: 12 });
    expect(w.find("[data-test=empty]").exists()).toBe(true);
    expect(w.find("[data-test=error]").exists()).toBe(false);
  });
});
