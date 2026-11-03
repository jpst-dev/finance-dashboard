import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { getApi } from "../api/context";
import { ApiError, NETWORK_ERROR_CODE } from "../api/errors";
import type {
  Account,
  ConfirmBody,
  ConfirmResult,
  ImportBatch,
  ImportLine,
  ImportLineOverride,
  ImportPreview,
  TransactionState,
} from "../api/types";
import { useAccountsStore } from "./accounts";
import { errorCodeOf } from "./errorCode";
import { useTransactionsStore } from "./transactions";

/** Mesmo limite do servidor (NubankOfxReader.MaxBytes). */
export const MAX_FILE_BYTES = 5 * 1024 * 1024;

/** Códigos que o servidor devolve em `{error}` nas rotas de importação (ImportEndpoints/NubankOfxReader). */
const SERVER_CODES = [
  "not_ofx",
  "empty",
  "too_large",
  "malformed",
  "forbidden_dtd",
  "unsupported_multiple_statements",
  "account_type_mismatch",
  "already_imported",
  "already_confirmed",
  "already_undone",
  "account_archived",
  "unsupported_amount_precision",
  "unsupported_line",
  "invalid_override",
  "preview_expired",
  "blocking_warnings",
] as const;

/** Códigos só do navegador (validação antes de enviar). */
const CLIENT_CODES = ["no_file", "one_file", "invalid_account"] as const;

/** Todos os códigos que a importação pode mostrar; cada um tem `real.errors.<code>` nos dois idiomas (teste garante). */
export const IMPORT_ERROR_CODES: readonly string[] = [...SERVER_CODES, ...CLIENT_CODES];

export type LineOverride = { state?: TransactionState; categoryId?: string | null };

/** Valida a seleção de arquivo sem ler o conteúdo; devolve o código de erro ou `null`. */
export function validateFiles(files: ArrayLike<File> | null | undefined): string | null {
  if (!files || files.length === 0) return "no_file";
  if (files.length > 1) return "one_file";
  const f = files[0];
  if (!/\.ofx$/i.test(f.name)) return "not_ofx";
  if (f.size === 0) return "empty";
  if (f.size > MAX_FILE_BYTES) return "too_large";
  return null;
}

/**
 * Converte qualquer erro em código estável (nunca texto do servidor). Código desconhecido cai no genérico de
 * errorCodeOf. Upload: 409 sem corpo = conta arquivada (ImportEndpoints). Confirm: 404 = a prévia sumiu
 * (expirada e descartada, ou substituída por reenvio do mesmo arquivo) → tratada como expirada.
 */
export function importErrorCode(e: unknown, ctx: "upload" | "confirm" | "undo"): string {
  if (!(e instanceof ApiError)) return "server";
  if (e.status === 0 || e.code === NETWORK_ERROR_CODE) return "network";
  if (e.code && (SERVER_CODES as readonly string[]).includes(e.code)) return e.code;
  if (e.status === 410) return "preview_expired";
  if (e.status === 413) return "too_large";
  if (ctx === "upload" && e.status === 409) return "account_archived";
  if (ctx === "confirm" && e.status === 404) return "preview_expired";
  return errorCodeOf(e);
}

const IMPORTABLE: Account["type"][] = ["Checking", "CreditCard"];

export const useImportsStore = defineStore("fd-imports", () => {
  const preview = ref<ImportPreview | null>(null);
  /** Classificação escolhida na prévia, por sourceKey (estado de UI; só vai ao servidor no confirm). */
  const overrides = ref(new Map<string, LineOverride>());
  const result = ref<ConfirmResult | null>(null);
  /** Código do último erro da importação (upload/confirm/cancelar); a tela traduz. */
  const error = ref<string | null>(null);
  const expired = ref(false);
  /** O servidor pediu confirmação de avisos bloqueantes (409 blocking_warnings) mesmo sem eles na prévia. */
  const serverAck = ref(false);
  const busy = ref(false);
  /** Conta da última tentativa de envio (mantida ao voltar para o envio). */
  const lastAccountId = ref<string | null>(null);

  const history = ref<ImportBatch[]>([]);
  const historyLoading = ref(false);
  const historyError = ref<string | null>(null);
  const undoing = ref(false);

  const importableAccounts = computed(() =>
    useAccountsStore().items.filter((a) => !a.isArchived && IMPORTABLE.includes(a.type)),
  );

  const lines = computed<ImportLine[]>(() => preview.value?.lines ?? []);
  const newLines = computed(() => lines.value.filter((l) => !l.existing && l.state !== "PendingClassification"));
  const pendingLines = computed(() => lines.value.filter((l) => !l.existing && l.state === "PendingClassification"));
  const existingLines = computed(() => lines.value.filter((l) => l.existing));
  const warnings = computed(() => preview.value?.warnings ?? []);
  const hasBlocking = computed(() => warnings.value.some((w) => w.severity === "blocking"));
  const ackRequired = computed(() => hasBlocking.value || serverAck.value);

  /** Linha NOVA da prévia atual com esse sourceKey (as existentes nunca recebem override). */
  function newLine(sourceKey: string): ImportLine | undefined {
    return lines.value.find((l) => l.sourceKey === sourceKey && !l.existing);
  }

  function setOverride(sourceKey: string, patch: LineOverride): void {
    const cur = overrides.value.get(sourceKey) ?? {};
    const next: LineOverride = { ...cur, ...patch };
    const base = newLine(sourceKey);
    if (base && next.state === base.state) delete next.state;
    if (next.categoryId === null || next.categoryId === undefined) delete next.categoryId;
    if (next.state === undefined && next.categoryId === undefined) overrides.value.delete(sourceKey);
    else overrides.value.set(sourceKey, next);
  }

  function clearOverride(sourceKey: string): void {
    overrides.value.delete(sourceKey);
  }

  /** Corpo do confirm: só linhas que, na prévia ATUAL, são novas e diferem do padrão proposto. */
  function confirmBody(ack: boolean): ConfirmBody {
    const out: ImportLineOverride[] = [];
    for (const [sourceKey, o] of overrides.value) {
      const base = newLine(sourceKey);
      if (!base) continue;
      const item: ImportLineOverride = { sourceKey };
      if (o.state && o.state !== base.state) item.state = o.state;
      if (o.categoryId) item.categoryId = o.categoryId;
      if (item.state !== undefined || item.categoryId !== undefined) out.push(item);
    }
    return out.length > 0 ? { acknowledgeBlockingWarnings: ack, overrides: out } : { acknowledgeBlockingWarnings: ack };
  }

  function clearPreview(): void {
    preview.value = null;
    overrides.value.clear();
    expired.value = false;
    serverAck.value = false;
  }

  async function upload(accountId: string, file: File | ArrayLike<File> | null): Promise<boolean> {
    if (busy.value) return false;
    error.value = null;
    result.value = null;
    lastAccountId.value = accountId || null;
    const files: ArrayLike<File> | null = file instanceof File ? [file] : file;
    const bad = validateFiles(files);
    if (bad) {
      error.value = bad;
      return false;
    }
    if (!importableAccounts.value.some((a) => a.id === accountId)) {
      error.value = "invalid_account";
      return false;
    }
    busy.value = true;
    try {
      const p = await getApi().imports.preview(accountId, files![0]);
      clearPreview();
      preview.value = p;
      return true;
    } catch (e) {
      error.value = importErrorCode(e, "upload");
      return false;
    } finally {
      busy.value = false;
    }
  }

  async function refreshAfterChange(): Promise<void> {
    await Promise.all([useTransactionsStore().loadPendingCount(), loadHistory(), useAccountsStore().load()]);
  }

  async function confirm(ack: boolean): Promise<boolean> {
    const p = preview.value;
    if (busy.value || !p) return false;
    busy.value = true;
    error.value = null;
    try {
      const r = await getApi().imports.confirm(p.batchId, confirmBody(ack));
      result.value = r;
      clearPreview();
      busy.value = false;
      await refreshAfterChange();
      return true;
    } catch (e) {
      const code = importErrorCode(e, "confirm");
      error.value = code;
      if (code === "preview_expired") expired.value = true;
      if (code === "blocking_warnings") serverAck.value = true;
      return false;
    } finally {
      busy.value = false;
    }
  }

  /** Descarta a prévia: o servidor também a remove (undo de lote em Preview); falha lá não impede o descarte local. */
  async function cancel(): Promise<void> {
    const p = preview.value;
    if (busy.value || !p) return;
    busy.value = true;
    try {
      await getApi().imports.undo(p.batchId);
    } catch {
      /* a prévia expira sozinha em 24 h */
    } finally {
      busy.value = false;
    }
    clearPreview();
    error.value = null;
  }

  /** Volta ao passo de envio (ex.: prévia expirada), mantendo a conta escolhida. */
  function backToUpload(): void {
    clearPreview();
    error.value = null;
  }

  async function loadHistory(): Promise<void> {
    historyLoading.value = true;
    historyError.value = null;
    try {
      history.value = await getApi().imports.list();
    } catch (e) {
      historyError.value = importErrorCode(e, "undo");
    } finally {
      historyLoading.value = false;
    }
  }

  /** `editedCount` do lote (GET /api/imports/{id}); `null` se não deu para obter. */
  async function editedCount(id: string): Promise<number | null> {
    try {
      return (await getApi().imports.get(id)).editedCount;
    } catch {
      return null;
    }
  }

  async function undo(id: string): Promise<boolean> {
    if (undoing.value) return false;
    undoing.value = true;
    historyError.value = null;
    try {
      await getApi().imports.undo(id);
    } catch (e) {
      const code = importErrorCode(e, "undo");
      undoing.value = false;
      await loadHistory(); // o estado do lote pode ter mudado (ex.: já desfeito em outra aba)
      historyError.value = code;
      return false;
    }
    undoing.value = false;
    await refreshAfterChange();
    return true;
  }

  function reset(): void {
    clearPreview();
    result.value = null;
    error.value = null;
  }

  return {
    preview, overrides, result, error, expired, busy, lastAccountId,
    history, historyLoading, historyError, undoing,
    importableAccounts, newLines, pendingLines, existingLines, warnings, hasBlocking, ackRequired,
    setOverride, clearOverride, confirmBody, upload, confirm, cancel, backToUpload,
    loadHistory, editedCount, undo, reset,
  };
});
