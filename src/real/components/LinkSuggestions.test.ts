import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { acc } from "../testing";
import { fakeLinksApi, link } from "../testingLinks";
import { useAccountsStore } from "../stores/accounts";
import { useLinksStore } from "../stores/links";
import LinkSuggestions from "./LinkSuggestions.vue";

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

async function setup(l = link()) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const api = fakeLinksApi([l]);
  const accounts = useAccountsStore();
  accounts.items = [acc({ id: "a1", name: "Conta Corrente" }), acc({ id: "a2", name: "Poupança" })];
  await useLinksStore().load();
  const w = mount(LinkSuggestions, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, api };
}

describe("LinkSuggestions", () => {
  it("renders_pair_with_dates_accounts_money_and_buttons", async () => {
    const { w } = await setup();
    const row = w.find("[data-test=suggestion]");
    const text = row.text();
    expect(text).toContain("05/03/2026");
    expect(text).toContain("06/03/2026");
    expect(text).toContain("Conta Corrente");
    expect(text).toContain("Poupança");
    expect(text).toContain("100,00");
    expect(row.find("[data-test=link-confirm]").text()).toBe("Confirmar");
    expect(row.find("[data-test=link-reject]").text()).toBe("Rejeitar");
  });

  it("confirm_and_reject_call_store", async () => {
    const { w, api } = await setup();
    await w.find("[data-test=link-confirm]").trigger("click");
    await flushPromises();
    expect(api.links.confirm).toHaveBeenCalledWith("l1");
    expect(w.find("[data-test=suggestion]").exists()).toBe(false);
  });

  it("reject_calls_api", async () => {
    const { w, api } = await setup();
    await w.find("[data-test=link-reject]").trigger("click");
    await flushPromises();
    expect(api.links.reject).toHaveBeenCalledWith("l1");
  });

  it("renders_third_party_description_as_text_not_html", async () => {
    const l = link();
    l.out.description = "Pix <b>negrito</b>";
    const { w } = await setup(l);
    expect(w.find("[data-test=suggestion]").text()).toContain("<b>negrito</b>");
    expect(w.find("[data-test=suggestion] b").exists()).toBe(false);
  });

  it("renders_nothing_without_suggestions", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    fakeLinksApi([]);
    await useLinksStore().load();
    const w = mount(LinkSuggestions, { global: { plugins: [pinia, i18n] } });
    expect(w.find("[data-test=suggestions]").exists()).toBe(false);
  });
});
