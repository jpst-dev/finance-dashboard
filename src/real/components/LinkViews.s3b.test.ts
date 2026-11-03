import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import type { LinkResponse } from "../api/types";
import { acc } from "../testing";
import { fakeLinksApi, link, oneSided } from "../testingLinks";
import { useAccountsStore } from "../stores/accounts";
import { useLinksStore } from "../stores/links";
import LinkedPairs from "./LinkedPairs.vue";
import LinkSuggestions from "./LinkSuggestions.vue";

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

async function setup(component: typeof LinkSuggestions | typeof LinkedPairs, l: LinkResponse) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const api = fakeLinksApi([l]);
  useAccountsStore().items = [acc({ id: "a1", name: "Conta Corrente" }), acc({ id: "a2", name: "Poupança" })];
  await useLinksStore().load();
  const w = mount(component, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, api };
}

describe("LinkSuggestions one-sided", () => {
  const row = (w: ReturnType<typeof mount>) => w.find("[data-test=suggestion]");

  it("renders_single_line_with_external_note_and_buttons", async () => {
    const { w } = await setup(LinkSuggestions, oneSided());
    expect(row(w).text()).toContain("Transferência enviada");
    expect(row(w).text()).not.toContain("Entrada:");
    expect(row(w).find("[data-test=link-external]").text()).toBe("conta fora do app");
    expect(row(w).find("[data-test=link-confirm]").exists()).toBe(true);
    expect(row(w).find("[data-test=link-reject]").exists()).toBe(true);
  });

  it("invoice_payment_note_is_sem_extrato", async () => {
    const { w } = await setup(LinkSuggestions, oneSided({ kind: "InvoicePayment" }));
    expect(w.find("[data-test=link-external]").text()).toBe("sem extrato");
  });

  it("only_in_side_also_renders", async () => {
    const { w } = await setup(LinkSuggestions, link({ out: null, external: true }));
    expect(row(w).text()).toContain("Transferência recebida");
    expect(row(w).text()).not.toContain("Saída:");
  });

  it("description_is_text_not_html", async () => {
    const l = oneSided();
    l.out!.description = "<b>x</b>";
    const { w } = await setup(LinkSuggestions, l);
    expect(row(w).find("b").exists()).toBe(false);
    expect(row(w).text()).toContain("<b>x</b>");
  });

  it("pix_credit_kind_label", async () => {
    const { w } = await setup(LinkSuggestions, link({ kind: "PixCredit" }));
    expect(row(w).text()).toContain("Pix no crédito");
  });
});

describe("LinkedPairs one-sided", () => {
  it("shows_one_sided_entry_with_external_note_and_undo", async () => {
    const { w, api } = await setup(LinkedPairs, oneSided({ state: "Confirmed", kind: "InvoicePayment" }));
    const row = w.find("[data-test=linked]");
    expect(row.text()).toContain("Pagamento de fatura");
    expect(row.text()).toContain("Transferência enviada");
    expect(row.find("[data-test=link-external]").text()).toBe("sem extrato");
    await row.find("[data-test=link-undo]").trigger("click");
    await flushPromises();
    expect(api.links.undo).toHaveBeenCalledWith("o1");
    expect(w.find("[data-test=linked]").exists()).toBe(false);
  });

  it("one_sided_description_is_text", async () => {
    const l = oneSided({ state: "Confirmed" });
    l.out!.description = "<b>y</b>";
    const { w } = await setup(LinkedPairs, l);
    expect(w.find("[data-test=linked] b").exists()).toBe(false);
    expect(w.find("[data-test=linked]").text()).toContain("<b>y</b>");
  });

  it("pix_credit_kind_label", async () => {
    const { w } = await setup(LinkedPairs, link({ state: "Confirmed", kind: "PixCredit" }));
    expect(w.find("[data-test=linked]").text()).toContain("Pix no crédito");
  });
});
