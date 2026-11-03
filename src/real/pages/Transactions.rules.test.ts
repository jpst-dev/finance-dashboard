import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import i18n from "../i18n";
import { setApi } from "../api/context";
import type { ApiClient } from "../api";
import type { Category } from "../api/types";
import { acc } from "../testing";
import { fakeTxApi, tx } from "../testingTx";
import { useCategoryRulesStore } from "../stores/categoryRules";
import Transactions from "./Transactions.vue";

const cats = [{ id: "c1", name: "Mercado", kind: "Expense" } as Category];

async function setup(item = tx({ categoryId: null, description: "Renomeada", originalDescription: "Supermercado Exemplo 12/03 4567" })) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const transactions = fakeTxApi([item]);
  setApi({ transactions, accounts: { list: async () => [acc({ id: "a1", name: "Conta" })] }, categories: { list: async () => cats } } as unknown as ApiClient);
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/", component: { template: "<div/>" } },
      { path: "/rules", component: { template: "<div/>" } },
    ],
  });
  const w = mount(Transactions, { global: { plugins: [pinia, i18n, router] } });
  await flushPromises();
  return { w, router };
}

async function saveWith(w: Awaited<ReturnType<typeof setup>>["w"], category: string) {
  await w.find("[data-test=tx-edit]").trigger("click");
  await w.find("#tx-category").setValue(category);
  await w.find("[data-test=tx-form]").trigger("submit");
  await flushPromises();
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

describe("Transactions rule shortcut", () => {
  it("appears_after_saving_a_category_and_uses_original_description", async () => {
    const { w } = await setup();
    expect(w.find("[data-test=rule-shortcut]").exists()).toBe(false);
    await saveWith(w, "c1");
    const text = w.find("[data-test=rule-shortcut-text]").text();
    expect(text).toBe("Sempre categorizar «SUPERMERCADO EXEMPLO» como Mercado");
    expect(text).not.toContain("RENOMEADA");
  });

  it("does_not_appear_when_saved_without_category", async () => {
    const { w } = await setup(tx({ categoryId: "c1", originalDescription: "Supermercado Exemplo" }));
    await saveWith(w, "");
    expect(w.find("[data-test=rule-shortcut]").exists()).toBe(false);
  });

  it("does_not_appear_when_snippet_is_too_short", async () => {
    const { w } = await setup(tx({ categoryId: null, originalDescription: "AB 12/03" }));
    await saveWith(w, "c1");
    expect(w.find("[data-test=rule-shortcut]").exists()).toBe(false);
  });

  it("button_sets_suggestion_in_store_and_navigates_without_query", async () => {
    const { w, router } = await setup();
    await saveWith(w, "c1");
    await w.find("[data-test=rule-shortcut-go]").trigger("click");
    await flushPromises();
    expect(useCategoryRulesStore().suggestion).toEqual({ pattern: "SUPERMERCADO EXEMPLO", categoryId: "c1" });
    expect(router.currentRoute.value.fullPath).toBe("/rules");
    expect(w.find("[data-test=rule-shortcut]").exists()).toBe(false);
  });

  it("renders_snippet_as_text", async () => {
    const { w } = await setup(tx({ categoryId: null, originalDescription: "<b>LOJA</b> Exemplo" }));
    await saveWith(w, "c1");
    expect(w.find("[data-test=rule-shortcut-text]").text()).toContain("<B>LOJA</B> EXEMPLO");
    expect(w.find("[data-test=rule-shortcut] b").exists()).toBe(false);
  });
});
