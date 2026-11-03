import { beforeEach, describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia } from "pinia";
import i18n from "../i18n";
import { tx } from "../testingTx";
import type { Transaction, TransactionLink } from "../api/types";
import TransactionTable from "./TransactionTable.vue";

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

const mk = (items: Transaction[]) =>
  mount(TransactionTable, {
    props: { items, accountName: () => "Conta", categoryName: () => "Cat" },
    global: { plugins: [createPinia(), i18n] },
  });
const confirmed = (kind: TransactionLink["kind"], cp: string | null): TransactionLink => ({
  id: "l9",
  kind,
  state: "Confirmed",
  counterpartTransactionId: cp,
});
const rowsOf = (w: ReturnType<typeof mk>) => w.findAll("[data-test=tx-row]");

describe("TransactionTable S3(b)", () => {
  it("own_button_only_for_own_transfer_pairing_without_link", () => {
    const w = mk([
      tx({ id: "a", pairing: "OwnTransfer" }),
      tx({ id: "b", pairing: "OwnTransfer", link: confirmed("OwnTransfer", null) }),
      tx({ id: "c", pairing: "InvoicePayment" }),
      tx({ id: "d", pairing: null }),
    ]);
    expect(rowsOf(w).map((r) => r.find("[data-test=tx-own]").exists())).toEqual([true, false, false, false]);
    expect(w.find("[data-test=tx-own]").text()).toBe("É meu");
  });

  it("no_counterpart_only_for_pending_invoice_payment_without_link", () => {
    const w = mk([
      tx({ id: "a", pairing: "InvoicePayment", state: "PendingClassification" }),
      tx({ id: "b", pairing: "InvoicePayment", state: "Normal" }),
      tx({ id: "c", pairing: "InvoicePayment", state: "PendingClassification", link: confirmed("InvoicePayment", null) }),
      tx({ id: "d", pairing: "OwnTransfer", state: "PendingClassification" }),
    ]);
    expect(rowsOf(w).map((r) => r.find("[data-test=tx-no-counterpart]").exists())).toEqual([true, false, false, false]);
    expect(w.find("[data-test=tx-no-counterpart]").text()).toBe("Sem contrapartida");
  });

  it("buttons_emit_events", async () => {
    const w = mk([tx({ id: "a", pairing: "OwnTransfer" }), tx({ id: "b", pairing: "InvoicePayment", state: "PendingClassification" })]);
    await w.find("[data-test=tx-own]").trigger("click");
    await w.find("[data-test=tx-no-counterpart]").trigger("click");
    expect((w.emitted("own")![0][0] as Transaction).id).toBe("a");
    expect((w.emitted("noCounterpart")![0][0] as Transaction).id).toBe("b");
  });

  it("badges_for_pix_credit_and_one_sided_variants", () => {
    const w = mk([
      tx({ id: "a", link: confirmed("PixCredit", "t2") }),
      tx({ id: "b", link: confirmed("OwnTransfer", null) }),
      tx({ id: "c", link: confirmed("InvoicePayment", null) }),
    ]);
    const rows = rowsOf(w);
    expect(rows[0].find("[data-test=tx-link]").text()).toBe("Pix no crédito");
    expect(rows[0].find("[data-test=tx-link-external]").exists()).toBe(false);
    expect(rows[1].find("[data-test=tx-link]").text()).toBe("Transferência");
    expect(rows[1].find("[data-test=tx-link-external]").text()).toBe("conta fora do app");
    expect(rows[2].find("[data-test=tx-link-external]").text()).toBe("sem extrato");
  });

  it("description_with_html_is_text", () => {
    const w = mk([tx({ description: "<b>z</b>", pairing: "OwnTransfer" })]);
    expect(w.find("[data-test=tx-desc] b").exists()).toBe(false);
    expect(w.find("[data-test=tx-desc]").text()).toBe("<b>z</b>");
  });
});
