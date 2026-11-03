import { beforeEach, describe, expect, it, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { ApiError } from "../api/errors";
import { useValuesVisibility } from "../money/visibility";
import { acc, fakeAccountsApi } from "../testing";
import Accounts from "./Accounts.vue";

async function setup(list = [acc()]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const fake = fakeAccountsApi(list);
  const w = mount(Accounts, { global: { plugins: [pinia, i18n] } });
  await flushPromises();
  return { w, ...fake };
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
});

describe("Accounts page", () => {
  it("card_balance_renders_with_debt_label_and_masks_when_hidden", async () => {
    const { w } = await setup([acc({ type: "CreditCard", name: "Cartão Exemplo", balance: "-1234.56" })]);
    const b = w.find("[data-test=balance]");
    expect(b.text()).toContain("Dívida");
    expect(b.text()).toMatch(/1\.234,56/);
    useValuesVisibility().toggle();
    await flushPromises();
    expect(w.find("[data-test=balance]").text()).not.toMatch(/1\.234|56/);
    expect(w.html()).not.toContain("1234.56");
    expect(w.find("[data-test=balance]").text()).toContain("Dívida");
  });

  it("checkpoint_form_rejects_invalid_money_and_sends_string", async () => {
    const { w, accounts } = await setup();
    await w.find("[data-test=register-balance]").trigger("click");
    expect(w.text()).toContain("saldo ao fim do dia");
    const input = w.find("input[id^=cp-amount-]");
    await input.setValue("12,345");
    await w.find("[data-test=checkpoint-form]").trigger("submit");
    expect(accounts.addCheckpoint).not.toHaveBeenCalled();
    expect(w.text()).toContain("Valor inválido");
    await input.setValue("1.234,50");
    await w.find("[data-test=checkpoint-form]").trigger("submit");
    await flushPromises();
    expect(accounts.addCheckpoint).toHaveBeenCalledTimes(1);
    const [id, body] = accounts.addCheckpoint.mock.calls[0];
    expect(id).toBe("a1");
    expect(body.amount).toBe("1234.50");
    expect(body.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(accounts.list).toHaveBeenCalledTimes(2); // recarregou saldos
  });

  it("checkpoint_form_default_date_is_local_today", async () => {
    vi.useFakeTimers({ toFake: ["Date"] });
    try {
      vi.setSystemTime(new Date(2026, 0, 5, 23, 59));
      const { w } = await setup();
      await w.find("[data-test=register-balance]").trigger("click");
      expect((w.find("input[id^=cp-date-]").element as HTMLInputElement).value).toBe("2026-01-05");
    } finally {
      vi.useRealTimers();
    }
  });

  it("api_errors_are_shown_translated_without_leaking_values", async () => {
    const { w, accounts } = await setup();
    accounts.create.mockRejectedValueOnce(new ApiError(400, "bad-9999.99", undefined, "raw server 9999.99"));
    await w.find("#acc-name").setValue("Conta Exemplo");
    await w.find("form").trigger("submit");
    await flushPromises();
    const err = w.find("[data-test=error]");
    expect(err.text()).toBe("Os dados enviados não foram aceitos. Revise e tente novamente.");
    expect(w.html()).not.toContain("9999.99");
  });

  it("archive_asks_confirmation_and_unarchive_from_collapsible_section", async () => {
    const { w, accounts } = await setup([acc(), acc({ id: "a2", name: "Antiga Exemplo", isArchived: true })]);
    expect(w.find("[data-test=archived]").attributes("style")).toContain("display: none");
    await w.find("[data-test=toggle-archived]").trigger("click");
    expect(w.find("[data-test=archived]").attributes("style") ?? "").not.toContain("display: none");
    await w.find("[data-test=unarchive]").trigger("click");
    await flushPromises();
    expect(accounts.update).toHaveBeenCalledWith("a2", expect.objectContaining({ isArchived: false }));
    await w.find("[data-test=archive]").trigger("click");
    expect(w.text()).toContain("Arquivar esta conta?");
    expect(accounts.update).toHaveBeenCalledTimes(1);
  });

  it("create_rejects_blank_name_and_disables_buttons_while_busy", async () => {
    const { w, accounts } = await setup([]);
    expect(w.text()).toContain("Nenhuma conta ainda");
    await w.find("#acc-name").setValue("   ");
    await w.find("form").trigger("submit");
    expect(accounts.create).not.toHaveBeenCalled();
    expect(w.text()).toContain("1 a 80");
    let release!: () => void;
    accounts.create.mockImplementationOnce(() => new Promise((r) => (release = () => r(acc()))));
    await w.find("#acc-name").setValue("Conta Exemplo");
    await w.find("form").trigger("submit");
    await flushPromises();
    expect(w.find("form button[type=submit]").attributes("disabled")).toBeDefined();
    release();
    await flushPromises();
  });

  it("shows_error_with_retry_when_load_fails", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const { accounts } = fakeAccountsApi();
    accounts.list.mockRejectedValueOnce(new ApiError(0));
    const w = mount(Accounts, { global: { plugins: [pinia, i18n] } });
    await flushPromises();
    expect(w.find("[data-test=error]").text()).toContain("conectar");
    await w.findAll("button").find((b) => b.text() === "Tentar novamente")!.trigger("click");
    await flushPromises();
    expect(w.findAll("[data-test=account]")).toHaveLength(1);
  });
});
