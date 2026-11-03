import { beforeEach, describe, expect, it } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { useCategoriesStore } from "../stores/categories";
import { fakeAccountsApi } from "../testing";
import CategorySelect from "./CategorySelect.vue";

const cats = [
  { id: "sal", name: "Salário Exemplo", kind: "Income" as const, isDefault: false },
  { id: "mer", name: "Mercado Exemplo", kind: "Expense" as const, isDefault: false },
];

async function setup(props: { modelValue: string | null; amount: string }) {
  const pinia = createPinia();
  setActivePinia(pinia);
  fakeAccountsApi([], cats);
  await useCategoriesStore().load();
  return mount(CategorySelect, { props, global: { plugins: [pinia, i18n] } });
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

describe("CategorySelect", () => {
  it("filters_by_amount_sign_and_has_empty_option", async () => {
    const w = await setup({ modelValue: null, amount: "-10.00" });
    const texts = w.findAll("option").map((o) => o.text());
    expect(texts).toEqual(["Sem categoria", "Mercado Exemplo"]);
    expect(w.find("select").attributes("aria-label")).toBe("Categoria");
  });

  it("emits_null_for_empty_and_id_for_choice", async () => {
    const w = await setup({ modelValue: null, amount: "10.00" });
    await w.find("select").setValue("sal");
    await w.find("select").setValue("");
    expect(w.emitted("update:modelValue")).toEqual([["sal"], [null]]);
  });

  it("keeps_current_value_visible_and_flagged_when_sign_changes", async () => {
    const w = await setup({ modelValue: "sal", amount: "-10.00" });
    const texts = w.findAll("option").map((o) => o.text());
    expect(texts).toContain("Salário Exemplo (não combina com o sinal do valor)");
    expect((w.find("select").element as HTMLSelectElement).value).toBe("sal");
    await flushPromises();
  });
});
