import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { ApiError } from "../api/errors";
import { fakeRulesApi, rule } from "../testingRules";
import { useCategoryRulesStore } from "../stores/categoryRules";
import Rules from "./Rules.vue";

async function setup(rules = [rule()], pre?: (s: ReturnType<typeof useCategoryRulesStore>) => void) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const api = fakeRulesApi(rules);
  const store = useCategoryRulesStore();
  pre?.(store);
  const w = mount(Rules, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, api, store };
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

describe("Rules page", () => {
  it("renders_table_with_pattern_category_matches_and_hint", async () => {
    const { w } = await setup();
    const row = w.find("[data-test=rule-row]");
    expect(row.find("[data-test=rule-pattern]").text()).toBe("SUPERMERCADO EXEMPLO");
    expect(row.find("[data-test=rule-category]").text()).toBe("Alimentação");
    expect(row.find("[data-test=rule-matches]").text()).toBe("3");
    expect(w.find("[data-test=matches-hint]").text()).toContain("mais do que");
  });

  it("shows_empty_message", async () => {
    const { w } = await setup([]);
    expect(w.find("[data-test=empty]").exists()).toBe(true);
  });

  it("form_offers_all_categories_and_creates", async () => {
    const { w, api } = await setup([]);
    const opts = w.findAll("#rule-category option").map((o) => o.text());
    expect(opts).toEqual(expect.arrayContaining(["Alimentação", "Salário"]));
    await w.find("[data-test=pattern]").setValue("  PADARIA ");
    await w.find("#rule-category").setValue("c1");
    await w.find("[data-test=rule-form]").trigger("submit");
    await flushPromises();
    expect(api.categoryRules.create).toHaveBeenCalledWith({ pattern: "PADARIA", categoryId: "c1" });
    expect((w.find("[data-test=pattern]").element as HTMLInputElement).value).toBe("");
    expect(w.find("[data-test=status]").text()).toBe("Regra criada.");
  });

  it("incomplete_form_shows_invalid_without_calling_api", async () => {
    const { w, api } = await setup([]);
    await w.find("[data-test=rule-form]").trigger("submit");
    expect(api.categoryRules.create).not.toHaveBeenCalled();
    expect(w.find("[data-test=error]").exists()).toBe(true);
  });

  it("prefills_from_suggestion_and_clears_it", async () => {
    const { w, store } = await setup([], (s) => {
      s.suggestion = { pattern: "PADARIA EXEMPLO", categoryId: "c1" };
    });
    expect((w.find("[data-test=pattern]").element as HTMLInputElement).value).toBe("PADARIA EXEMPLO");
    expect((w.find("#rule-category").element as HTMLSelectElement).value).toBe("c1");
    expect(store.suggestion).toBeNull();
    expect(w.find("[data-test=from-suggestion]").exists()).toBe(true);
  });

  it("maps_duplicate_and_invalid_pattern_errors", async () => {
    const { w, api } = await setup([]);
    await w.find("[data-test=pattern]").setValue("PADARIA");
    await w.find("#rule-category").setValue("c1");
    api.categoryRules.create.mockRejectedValueOnce(new ApiError(409, "duplicate_rule"));
    await w.find("[data-test=rule-form]").trigger("submit");
    await flushPromises();
    expect(w.find("[data-test=error]").text()).toContain("Já existe uma regra");
    api.categoryRules.create.mockRejectedValueOnce(new ApiError(400, "invalid_pattern"));
    await w.find("[data-test=rule-form]").trigger("submit");
    await flushPromises();
    expect(w.find("[data-test=error]").text()).toContain("3 a 100");
    expect((w.find("[data-test=pattern]").element as HTMLInputElement).value).toBe("PADARIA");
  });

  it("delete_asks_confirmation_before_calling_api", async () => {
    const { w, api } = await setup();
    const del = w.find("[data-test=rule-delete]");
    expect(del.attributes("aria-label")).toMatch(/^Excluir/);
    await del.trigger("click");
    expect(api.categoryRules.remove).not.toHaveBeenCalled();
    expect(w.find("[data-test=confirm-delete]").text()).toContain("SUPERMERCADO EXEMPLO");
    await w.find("[data-test=confirm-no]").trigger("click");
    expect(w.find("[data-test=confirm-delete]").exists()).toBe(false);
    await del.trigger("click");
    await w.find("[data-test=confirm-yes]").trigger("click");
    await flushPromises();
    expect(api.categoryRules.remove).toHaveBeenCalledWith("r1");
    expect(w.find("[data-test=rule-row]").exists()).toBe(false);
  });

  it.each([
    ["pt-BR", 0, "nenhuma linha categorizada"],
    ["pt-BR", 1, "1 linha categorizada"],
    ["pt-BR", 2, "2 linhas categorizadas"],
    ["en-US", 0, "no lines categorized"],
    ["en-US", 1, "1 line categorized"],
    ["en-US", 2, "2 lines categorized"],
  ] as const)("apply_result_plural_%s_%i", async (loc, n, text) => {
    i18n.global.locale.value = loc;
    const { w, api } = await setup();
    api.categoryRules.apply.mockResolvedValueOnce({ applied: n });
    expect(w.find("[data-test=apply-result]").attributes("role")).toBe("status");
    await w.find("[data-test=apply]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=apply-result]").text()).toBe(text);
  });

  it("apply_button_is_disabled_while_busy", async () => {
    const { w, api } = await setup();
    let release!: () => void;
    api.categoryRules.apply.mockImplementationOnce(() => new Promise((r) => { release = () => r({ applied: 1 }); }));
    await w.find("[data-test=apply]").trigger("click");
    expect(w.find("[data-test=apply]").attributes("disabled")).toBeDefined();
    expect(w.find("[data-test=add]").attributes("disabled")).toBeDefined();
    release();
    await flushPromises();
    expect(w.find("[data-test=apply]").attributes("disabled")).toBeUndefined();
    expect(api.categoryRules.apply).toHaveBeenCalledTimes(1);
  });

  it("renders_third_party_text_as_text", async () => {
    const { w } = await setup([rule({ pattern: "<b>X</b>", categoryName: "<i>c</i>" })]);
    expect(w.find("[data-test=rule-pattern]").text()).toBe("<b>X</b>");
    expect(w.find("[data-test=rule-row] b").exists()).toBe(false);
    expect(w.find("[data-test=rule-row] i").exists()).toBe(false);
    await w.find("[data-test=rule-delete]").trigger("click");
    expect(w.find("[data-test=confirm-delete] b").exists()).toBe(false);
  });
});
