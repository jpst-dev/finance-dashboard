import { beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import i18n from "../i18n";
import { ApiError } from "../api/errors";
import { acc } from "../testing";
import { batch, fakeImportApi, ofx, preview } from "../testingImport";
import Import from "./Import.vue";

async function setup(opts: Parameters<typeof fakeImportApi>[0] = {}) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const fake = fakeImportApi(opts);
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/", component: { template: "<div/>" } },
      { path: "/transactions", name: "transactions", component: { template: "<div/>" } },
      { path: "/pending", name: "pending", component: { template: "<div/>" } },
    ],
  });
  const w = mount(Import, { global: { plugins: [pinia, i18n, router] }, attachTo: document.body });
  await flushPromises();
  return { w, ...fake };
}

async function pickFile(w: VueWrapper, files: File[]) {
  const input = w.find("input[type=file]");
  Object.defineProperty(input.element, "files", { value: files, configurable: true });
  await input.trigger("change");
}

async function uploadFlow(w: VueWrapper, accountId = "a1") {
  await w.find("[data-test=account]").setValue(accountId);
  await pickFile(w, [ofx()]);
  await w.find("[data-test=upload-form]").trigger("submit");
  await flushPromises();
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
  document.body.innerHTML = "";
});

describe("Import page", () => {
  it("Upload_sends_account_and_file_and_shows_tabs_with_counts", async () => {
    const { w, imports } = await setup({
      accounts: [
        acc({ id: "a1", name: "Conta Exemplo", type: "Checking" }),
        acc({ id: "card", name: "Cartão Exemplo", type: "CreditCard" }),
        acc({ id: "w", name: "Carteira Exemplo", type: "Wallet" }),
        acc({ id: "old", name: "Antiga Exemplo", type: "Checking", isArchived: true }),
      ],
    });
    const options = w.findAll("[data-test=account] option").map((o) => o.attributes("value"));
    expect(options).toEqual(expect.arrayContaining(["a1", "card"]));
    expect(options).not.toContain("w");
    expect(options).not.toContain("old");
    expect(w.find("label[for=import-file]").exists()).toBe(true);

    await uploadFlow(w, "card");
    expect(imports.preview).toHaveBeenCalledTimes(1);
    const [accountId, file] = imports.preview.mock.calls[0];
    expect(accountId).toBe("card");
    expect(file.name).toBe("extrato.ofx");

    const tabs = w.findAll("[role=tab]").map((t) => t.text());
    expect(tabs).toHaveLength(4);
    expect(tabs[0]).toMatch(/Novos.*2/);
    expect(tabs[1]).toMatch(/Pendentes.*1/);
    expect(tabs[2]).toMatch(/Já existentes.*1/);
    expect(tabs[3]).toMatch(/Avisos.*0/);
    expect(w.find("[data-test=confirm]").text()).toBe("Confirmar 3 novos");
    expect(w.find("[data-test=expires]").exists()).toBe(true);
  });

  it("client_side_rejection_shows_translated_error_and_sends_nothing", async () => {
    const { w, imports } = await setup();
    await w.find("[data-test=account]").setValue("a1");
    await pickFile(w, [ofx("planilha.csv")]);
    await w.find("[data-test=upload-form]").trigger("submit");
    await flushPromises();
    expect(imports.preview).not.toHaveBeenCalled();
    expect(w.find("[data-test=error]").text()).toContain(".ofx");
  });

  it("upload_error_code_is_translated", async () => {
    const { w, imports } = await setup();
    imports.preview.mockRejectedValueOnce(new ApiError(422, "account_type_mismatch"));
    await uploadFlow(w);
    const err = w.find("[data-test=error]").text();
    expect(err).toContain("cartão");
    expect(err).not.toContain("account_type_mismatch");
  });

  it("Confirm_422_invalid_override_stays_on_preview_with_error", async () => {
    const { w, imports } = await setup();
    await uploadFlow(w);
    imports.confirm.mockRejectedValueOnce(new ApiError(422, "invalid_override", undefined, "raw -25.50"));
    await w.find("[data-test=confirm]").trigger("click");
    await flushPromises();
    expect(w.find("[role=tablist]").exists()).toBe(true);
    const err = w.find("[data-test=error]").text();
    expect(err).toContain("classificação");
    expect(err).not.toContain("25");
  });

  it("Expired_preview_410_offers_resend", async () => {
    const { w, imports } = await setup();
    await uploadFlow(w);
    imports.confirm.mockRejectedValueOnce(new ApiError(410, "preview_expired"));
    await w.find("[data-test=confirm]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=error]").text()).toContain("expirou");
    const resend = w.find("[data-test=resend]");
    expect(resend.exists()).toBe(true);
    await resend.trigger("click");
    await flushPromises();
    expect(w.find("[role=tablist]").exists()).toBe(false);
    expect(w.find("[data-test=upload-form]").exists()).toBe(true);
    expect((w.find("[data-test=account]").element as HTMLSelectElement).value).toBe("a1");
  });

  it("confirm_success_shows_created_and_skipped_with_links", async () => {
    const { w } = await setup();
    await uploadFlow(w);
    await w.find("[data-test=confirm]").trigger("click");
    await flushPromises();
    const res = w.find("[data-test=result]");
    expect(res.attributes("aria-live")).toBe("polite");
    expect(res.text()).toContain("3 lançamentos criados, 1 pulados (já existiam).");
    expect(w.find("a[href='/transactions']").exists()).toBe(true);
    expect(w.find("a[href='/pending']").exists()).toBe(true);
  });

  it("Undo_confirmation_shows_editedCount_and_cross_batch_warning", async () => {
    const { w, imports } = await setup({
      history: [batch({ batchId: "h1" }), batch({ batchId: "h2", state: "Undone" }), batch({ batchId: "h3", state: "Preview" })],
    });
    imports.get.mockResolvedValueOnce(preview({ batchId: "h1", state: "Confirmed", editedCount: 4, expiresAt: null }));
    const rows = w.findAll("[data-test=history-row]");
    expect(rows).toHaveLength(3);
    expect(w.findAll("[data-test=undo]")).toHaveLength(1);
    expect(rows[0].text()).toContain("Conta Exemplo");

    await w.find("[data-test=undo]").trigger("click");
    await flushPromises();
    const dlg = w.find("[role=alertdialog]");
    expect(dlg.exists()).toBe(true);
    expect(dlg.text()).toContain("4");
    expect(dlg.text()).toContain("outro lote");
    expect(document.activeElement).toBe(w.find("[data-test=undo-yes]").element);

    await w.find("[data-test=undo-yes]").trigger("click");
    await flushPromises();
    expect(imports.undo).toHaveBeenCalledWith("h1");
    expect(imports.list.mock.calls.length).toBeGreaterThanOrEqual(2);
    expect(w.find("[role=alertdialog]").exists()).toBe(false);
  });

  it("undo_dialog_escape_closes_and_returns_focus", async () => {
    const { w, imports } = await setup({ history: [batch({ batchId: "h1" })] });
    const trigger = w.find("[data-test=undo]");
    (trigger.element as HTMLElement).focus();
    await trigger.trigger("click");
    await flushPromises();
    await w.find("[role=alertdialog]").trigger("keydown", { key: "Escape" });
    await flushPromises();
    expect(w.find("[role=alertdialog]").exists()).toBe(false);
    expect(imports.undo).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(w.find("[data-test=undo]").element);
  });

  it("undo_dialog_keeps_yes_disabled_and_offers_retry_when_editedCount_fails", async () => {
    const { w, imports } = await setup({ history: [batch({ batchId: "h1" })] });
    imports.get.mockRejectedValueOnce(new ApiError(0, "network"));
    await w.find("[data-test=undo]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=undo-yes]").attributes("disabled")).toBeDefined();
    expect(w.find("[data-test=edited-error]").text()).toContain("Não foi possível verificar");
    imports.get.mockResolvedValueOnce(preview({ batchId: "h1", state: "Confirmed", editedCount: 2, expiresAt: null }));
    await w.find("[data-test=edited-retry]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=edited-error]").exists()).toBe(false);
    expect(w.find("[data-test=edited-count]").text()).toContain("2");
    expect(w.find("[data-test=undo-yes]").attributes("disabled")).toBeUndefined();
  });

  it("undo_dialog_escape_works_while_editedCount_is_loading", async () => {
    const { w, imports } = await setup({ history: [batch({ batchId: "h1" })] });
    imports.get.mockImplementationOnce(() => new Promise(() => undefined));
    (w.find("[data-test=undo]").element as HTMLElement).focus();
    await w.find("[data-test=undo]").trigger("click");
    await flushPromises();
    expect(document.activeElement).toBe(w.find("[role=alertdialog]").element);
    document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    await flushPromises();
    expect(w.find("[role=alertdialog]").exists()).toBe(false);
    expect(document.activeElement).toBe(w.find("[data-test=undo]").element);
  });

  it("undo_error_is_translated", async () => {
    const { w, imports } = await setup({ history: [batch({ batchId: "h1" })] });
    imports.undo.mockRejectedValueOnce(new ApiError(409, "already_undone"));
    await w.find("[data-test=undo]").trigger("click");
    await flushPromises();
    await w.find("[data-test=undo-yes]").trigger("click");
    await flushPromises();
    expect(w.find("[data-test=history-error]").text()).toContain("desfeita");
  });

  it("cancel_of_already_confirmed_batch_shows_notice_and_does_not_undo", async () => {
    const { w, imports } = await setup();
    await uploadFlow(w);
    imports.get.mockResolvedValueOnce(preview({ batchId: "b1", state: "Confirmed", expiresAt: null }));
    await w.find("[data-test=cancel]").trigger("click");
    await flushPromises();
    expect(imports.undo).not.toHaveBeenCalled();
    expect(w.find("[role=tablist]").exists()).toBe(false);
    expect(w.find("[data-test=notice]").text()).toContain("nada foi desfeito");
  });
});
