import { beforeEach, describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia } from "pinia";
import i18n from "../i18n";
import { tx } from "../testingTx";
import type { Transaction } from "../api/types";
import TransactionTable from "./TransactionTable.vue";

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});

const mk = (items: Transaction[]) =>
  mount(TransactionTable, {
    props: { items, accountName: () => "Conta", categoryName: () => "Cat" },
    global: { plugins: [createPinia(), i18n] },
  });
const lk = (kind: "OwnTransfer" | "InvoicePayment", state: "Suggested" | "Confirmed" | "Rejected") => ({
  id: "l1",
  kind,
  state,
  counterpartTransactionId: "t2",
});

describe("TransactionTable selo de vínculo", () => {
  it("shows_badge_for_confirmed_links_only", () => {
    const w = mk([
      tx({ id: "a", link: lk("OwnTransfer", "Confirmed") }),
      tx({ id: "b", link: lk("InvoicePayment", "Confirmed") }),
      tx({ id: "c", link: lk("OwnTransfer", "Suggested") }),
      tx({ id: "d", link: lk("OwnTransfer", "Rejected") }),
      tx({ id: "e" }),
      tx({ id: "f", link: null }),
    ]);
    const badges = w.findAll("[data-test=tx-row]").map((r) => r.find("[data-test=tx-link]").exists() && r.find("[data-test=tx-link]").text());
    expect(badges).toEqual(["Transferência", "Pagamento de fatura", false, false, false, false]);
  });
});
