import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { acc } from "../testing";
import { fakeLinksApi, link } from "../testingLinks";
import { useAccountsStore } from "../stores/accounts";
import { useLinksStore } from "../stores/links";
import LinkedPairs from "./LinkedPairs.vue";

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

async function setup(l = link({ state: "Confirmed", kind: "InvoicePayment" })) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const api = fakeLinksApi([l]);
  useAccountsStore().items = [acc({ id: "a1", name: "Conta Corrente" }), acc({ id: "a2", name: "Cartão" })];
  await useLinksStore().load();
  const w = mount(LinkedPairs, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, api };
}

describe("LinkedPairs", () => {
  it("shows_pair_with_kind_and_undo", async () => {
    const { w } = await setup();
    const row = w.find("[data-test=linked]");
    expect(row.text()).toContain("Pagamento de fatura");
    expect(row.text()).toContain("Conta Corrente");
    expect(row.text()).toContain("Cartão");
    expect(row.text()).toContain("100,00");
    expect(row.find("[data-test=link-undo]").text()).toBe("Desfazer");
  });

  it("undo_calls_api_and_removes_row", async () => {
    const { w, api } = await setup();
    await w.find("[data-test=link-undo]").trigger("click");
    await flushPromises();
    expect(api.links.undo).toHaveBeenCalledWith("l1");
    expect(w.find("[data-test=linked]").exists()).toBe(false);
  });

  it("description_is_text_not_html", async () => {
    const l = link({ state: "Confirmed" });
    l.in!.description = "<b>x</b>";
    const { w } = await setup(l);
    expect(w.find("[data-test=linked] b").exists()).toBe(false);
    expect(w.find("[data-test=linked]").text()).toContain("<b>x</b>");
  });
});
