import { beforeEach, describe, expect, it } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import pt from "../i18n/pt-BR.json";
import en from "../i18n/en-US.json";
import { ApiError } from "../api/errors";
import { acc } from "../testing";
import { fakeImportApi, line, ofx, preview } from "../testingImport";
import { useAccountsStore } from "./accounts";
import { useTransactionsStore } from "./transactions";
import { IMPORT_ERROR_CODES, IMPORT_WARNING_CODES, MAX_FILE_BYTES, importErrorCode, lineId, useImportsStore, validateFiles } from "./imports";

async function setup(opts: Parameters<typeof fakeImportApi>[0] = {}) {
  setActivePinia(createPinia());
  const fake = fakeImportApi(opts);
  await useAccountsStore().load();
  return { ...fake, store: useImportsStore() };
}

function sized(name: string, size: number): File {
  const f = ofx(name);
  Object.defineProperty(f, "size", { value: size });
  return f;
}

beforeEach(() => localStorage.clear());

describe("imports store", () => {
  it("Oversized_or_non_ofx_file_rejected_client_side_before_upload", async () => {
    const { store, imports } = await setup();
    expect(MAX_FILE_BYTES).toBe(5 * 1024 * 1024);
    expect(validateFiles([sized("a.ofx", MAX_FILE_BYTES + 1)])).toBe("too_large");
    expect(validateFiles([sized("a.ofx", MAX_FILE_BYTES)])).toBeNull();
    expect(validateFiles([ofx("a.csv")])).toBe("unsupported_file");
    expect(validateFiles([ofx("A.OFX")])).toBeNull();
    expect(validateFiles([ofx("a.ofx", "")])).toBe("empty");
    expect(validateFiles([])).toBe("no_file");
    expect(validateFiles([ofx("a.ofx"), ofx("b.ofx")])).toBe("one_file");

    expect(await store.upload("a1", sized("a.ofx", MAX_FILE_BYTES + 1))).toBe(false);
    expect(store.error).toBe("too_large");
    expect(await store.upload("a1", ofx("extrato.txt"))).toBe(false);
    expect(store.error).toBe("unsupported_file");
    expect(imports.preview).not.toHaveBeenCalled();
  });

  it("upload_only_to_active_checking_or_card_accounts", async () => {
    const { store, imports } = await setup({
      accounts: [
        acc({ id: "a1", type: "Checking" }),
        acc({ id: "card", type: "CreditCard" }),
        acc({ id: "w", type: "Wallet" }),
        acc({ id: "old", type: "Checking", isArchived: true }),
      ],
    });
    expect(store.importableAccounts.map((a) => a.id)).toEqual(["a1", "card"]);
    expect(await store.upload("w", ofx())).toBe(false);
    expect(await store.upload("old", ofx())).toBe(false);
    expect(store.error).toBe("invalid_account");
    expect(imports.preview).not.toHaveBeenCalled();
    expect(await store.upload("card", ofx())).toBe(true);
    expect(imports.preview).toHaveBeenCalledTimes(1);
  });

  it("Pending_tab_override_is_sent_on_confirm_only_for_new_lines", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { state: "Normal" });
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { categoryId: "c-exp" });
    store.setOverride({ sourceKey: "e1", ordinal: 0 }, { state: "PendingClassification", categoryId: "c-exp" }); // existente: nunca enviado
    store.setOverride({ sourceKey: "zz", ordinal: 0 }, { state: "Normal" }); // fora da prévia
    store.setOverride({ sourceKey: "n1", ordinal: 0 }, { state: "Normal", categoryId: null }); // igual ao padrão: descartado
    await store.confirm(false);
    expect(imports.confirm).toHaveBeenCalledWith("b1", {
      acknowledgeBlockingWarnings: false,
      overrides: [{ sourceKey: "p1", state: "Normal", categoryId: "c-exp" }],
    });
  });

  it("overrides_dropped_for_lines_no_longer_new", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { categoryId: "c-exp" });
    // Reenvio: agora a mesma linha já existe.
    imports.preview.mockResolvedValueOnce(
      preview({ batchId: "b2", lines: [line({ sourceKey: "p1", state: "PendingClassification", existing: true })] }),
    );
    await store.upload("a1", ofx());
    await store.confirm(false);
    const body = imports.confirm.mock.calls[0][1] as { overrides?: unknown[] };
    expect(body.overrides ?? []).toEqual([]);
  });

  it("override_back_to_default_clears_entry", async () => {
    const { store } = await setup();
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { state: "Normal" });
    expect(store.overrides.has("p1#0")).toBe(true);
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { state: "PendingClassification" });
    expect(store.overrides.has("p1#0")).toBe(false);
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { categoryId: "c-exp" });
    store.clearOverride({ sourceKey: "p1", ordinal: 0 });
    expect(store.overrides.size).toBe(0);
  });

  it("double_click_confirm_sends_once", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    const a = store.confirm(false);
    const b = store.confirm(false);
    await Promise.all([a, b]);
    expect(imports.confirm).toHaveBeenCalledTimes(1);
  });

  it("confirm_success_refreshes_pending_count_and_history", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    expect(await store.confirm(false)).toBe(true);
    expect(store.result).toEqual({ batchId: "b1", created: 3, skipped: 1 });
    expect(store.preview).toBeNull();
    expect(useTransactionsStore().pendingCount).toBe(7);
    expect(imports.list).toHaveBeenCalled();
  });

  it("confirm_errors_keep_preview_and_overrides", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { state: "Normal" });
    imports.confirm.mockRejectedValueOnce(new ApiError(422, "invalid_override"));
    expect(await store.confirm(false)).toBe(false);
    expect(store.error).toBe("invalid_override");
    expect(store.preview?.batchId).toBe("b1");
    expect(store.overrides.get("p1#0")).toEqual({ state: "Normal" });

    imports.confirm.mockRejectedValueOnce(new ApiError(409, "blocking_warnings"));
    await store.confirm(false);
    expect(store.error).toBe("blocking_warnings");
    expect(store.ackRequired).toBe(true);

    imports.confirm.mockRejectedValueOnce(new ApiError(404));
    await store.confirm(true);
    expect(store.error).toBe("preview_expired");
    expect(store.expired).toBe(true);
  });

  it("error_codes_never_carry_server_text", () => {
    expect(importErrorCode(new ApiError(422, "something_new"), "upload")).toBe("invalid");
    expect(importErrorCode(new ApiError(410, "preview_expired"), "confirm")).toBe("preview_expired");
    expect(importErrorCode(new ApiError(409), "upload")).toBe("account_archived");
    expect(importErrorCode(new ApiError(413, "too_large"), "upload")).toBe("too_large");
    expect(importErrorCode(new ApiError(0, "network"), "upload")).toBe("network");
    expect(importErrorCode(new Error("R$ 9999"), "upload")).toBe("server");
    expect(importErrorCode(new ApiError(409, "already_undone"), "undo")).toBe("already_undone");
  });

  it("Each_warning_code_has_a_translation_in_both_languages", () => {
    expect(IMPORT_WARNING_CODES.length).toBe(13);
    const ws = (o: unknown) => (o as { real: { import: { warnings: Record<string, string> } } }).real.import.warnings;
    for (const c of IMPORT_WARNING_CODES) {
      expect(typeof ws(pt)[c], `pt-BR ${c}`).toBe("string");
      expect(typeof ws(en)[c], `en-US ${c}`).toBe("string");
    }
  });

  it("validate_accepts_ofx_and_xlsx_any_case", () => {
    expect(validateFiles([ofx("FATURA.XLSX")])).toBeNull();
    expect(validateFiles([ofx("a.OFX")])).toBeNull();
    expect(validateFiles([ofx("fatura.xls")])).toBe("unsupported_file");
    expect(validateFiles([ofx("x.csv")])).toBe("unsupported_file");
  });

  it("overrides_are_keyed_by_source_key_and_ordinal", async () => {
    const { store } = await setup({
      preview: preview({
        lines: [
          line({ sourceKey: "k", ordinal: 0, state: "PendingClassification" }),
          line({ sourceKey: "k", ordinal: 1, state: "PendingClassification" }),
        ],
      }),
    });
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "k", ordinal: 0 }, { state: "Normal" });
    store.setOverride({ sourceKey: "k", ordinal: 1 }, { categoryId: "c-exp" });
    expect(store.overrides.size).toBe(2);
    expect(lineId({ sourceKey: "k", ordinal: 1 })).toBe("k#1");
  });

  it("confirm_body_omits_ordinal_zero_and_sends_nonzero", async () => {
    const { store, imports } = await setup({
      preview: preview({
        lines: [
          line({ sourceKey: "k", ordinal: 0, state: "PendingClassification" }),
          line({ sourceKey: "k", ordinal: 1, state: "PendingClassification" }),
        ],
      }),
    });
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "k", ordinal: 0 }, { state: "Normal" });
    store.setOverride({ sourceKey: "k", ordinal: 1 }, { state: "Normal" });
    await store.confirm(false);
    expect(imports.confirm.mock.calls[0][1]).toEqual({
      acknowledgeBlockingWarnings: false,
      overrides: [{ sourceKey: "k", state: "Normal" }, { sourceKey: "k", ordinal: 1, state: "Normal" }],
    });
  });

  it("blocked_for_good_when_non_overridable_blocking_warning", async () => {
    const w = (over: object) => ({ code: "X", severity: "blocking", message: "m", overridable: true, ...over });
    const { store } = await setup({ preview: preview({ warnings: [w({})] as never }) });
    await store.upload("a1", ofx());
    expect(store.blockedForGood).toBe(false);
    const s2 = await setup({ preview: preview({ warnings: [w({ overridable: false })] as never }) });
    await s2.store.upload("a1", ofx());
    expect(s2.store.blockedForGood).toBe(true);
    const s3 = await setup({ preview: preview({ warnings: [w({ severity: "warning", overridable: false })] as never }) });
    await s3.store.upload("a1", ofx());
    expect(s3.store.blockedForGood).toBe(false);
  });

  it("Each_known_error_code_has_a_translation_in_both_languages", () => {
    const brief = [
      "not_ofx", "empty", "too_large", "malformed", "forbidden_dtd", "unsupported_multiple_statements",
      "account_type_mismatch", "already_imported", "already_confirmed", "already_undone", "account_archived",
      "unsupported_amount_precision", "unsupported_line", "invalid_override", "preview_expired",
      "unsupported_bank", "not_xlsx", "encrypted_xlsx", "xlsx_limits_exceeded", "xlsx_unsafe_content",
      "unsupported_layout", "invalid_cell", "unsupported_file",
    ];
    for (const c of brief) expect(IMPORT_ERROR_CODES).toContain(c);
    const errs = (o: unknown) => (o as { real: { errors: Record<string, string> } }).real.errors;
    for (const c of IMPORT_ERROR_CODES) {
      expect(typeof errs(pt)[c], `pt-BR ${c}`).toBe("string");
      expect(typeof errs(en)[c], `en-US ${c}`).toBe("string");
    }
  });

  it("undo_refreshes_history_and_pending_and_translates_errors", async () => {
    const { store, imports } = await setup();
    expect(await store.undo("h1")).toBe(true);
    expect(imports.undo).toHaveBeenCalledWith("h1");
    expect(imports.list).toHaveBeenCalled();
    expect(useTransactionsStore().pendingCount).toBe(7);
    imports.undo.mockRejectedValueOnce(new ApiError(409, "already_undone"));
    expect(await store.undo("h1")).toBe(false);
    expect(store.historyError).toBe("already_undone");
  });

  it("cancel_when_get_reports_Preview_calls_undo_once", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    store.setOverride({ sourceKey: "p1", ordinal: 0 }, { state: "Normal" });
    imports.get.mockResolvedValueOnce(preview({ batchId: "b1", state: "Preview" }));
    await store.cancel();
    expect(imports.get).toHaveBeenCalledWith("b1");
    expect(imports.undo).toHaveBeenCalledTimes(1);
    expect(imports.undo).toHaveBeenCalledWith("b1");
    expect(store.preview).toBeNull();
    expect(store.overrides.size).toBe(0);
  });

  it("cancel_when_get_reports_Confirmed_never_calls_undo", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    imports.get.mockResolvedValueOnce(preview({ batchId: "b1", state: "Confirmed" }));
    await store.cancel();
    expect(imports.undo).not.toHaveBeenCalled();
    expect(store.preview).toBeNull();
    expect(store.notice).toBe("not_preview");
    expect(imports.list).toHaveBeenCalled();
  });

  it("cancel_when_get_fails_with_network_never_calls_undo", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    imports.get.mockRejectedValueOnce(new ApiError(0, "network"));
    await store.cancel();
    expect(imports.undo).not.toHaveBeenCalled();
    expect(store.preview).toBeNull();
    expect(store.error).toBe("network");
  });

  it("cancel_when_get_404_drops_locally_without_undo", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    imports.get.mockRejectedValueOnce(new ApiError(404));
    await store.cancel();
    expect(imports.undo).not.toHaveBeenCalled();
    expect(store.preview).toBeNull();
    expect(store.error).toBeNull();
  });

  it("confirm_already_confirmed_drops_preview_and_cancel_or_reset_never_undo", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    imports.confirm.mockRejectedValueOnce(new ApiError(409, "already_confirmed"));
    expect(await store.confirm(false)).toBe(false);
    expect(store.preview).toBeNull();
    expect(store.error).toBe("already_confirmed");
    expect(imports.list).toHaveBeenCalled();
    await store.cancel();
    store.reset();
    expect(imports.undo).not.toHaveBeenCalled();
  });

  it("confirm_network_error_rechecks_batch_and_drops_confirmed_preview", async () => {
    const { store, imports } = await setup();
    await store.upload("a1", ofx());
    imports.confirm.mockRejectedValueOnce(new ApiError(0, "network"));
    imports.get.mockResolvedValueOnce(preview({ batchId: "b1", state: "Confirmed" }));
    await store.confirm(false);
    expect(store.error).toBe("network");
    expect(store.preview).toBeNull();
    await store.cancel();
    expect(imports.undo).not.toHaveBeenCalled();
  });

  it("already_imported_preview_is_flagged", async () => {
    const { store } = await setup({
      preview: preview({ newCount: 0, warnings: [{ code: "AlreadyImported", severity: "warning", message: "x", overridable: true }] }),
    });
    await store.upload("a1", ofx());
    expect(store.alreadyImported).toBe(true);
  });
});
