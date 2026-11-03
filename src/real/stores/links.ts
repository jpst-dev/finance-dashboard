import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import type { LinkResponse, ReconcileResult } from "../api/types";
import { linkErrorCodeOf } from "./errorCode";
import { useTransactionsStore } from "./transactions";

/** Quantidade de pares recentes mostrados em "Vinculados". */
export const CONFIRMED_SIZE = 10;
const SUGGESTED_SIZE = 50;

export const useLinksStore = defineStore("fd-links", () => {
  const suggested = ref<LinkResponse[]>([]);
  const confirmed = ref<LinkResponse[]>([]);
  const loading = ref(false);
  const busy = ref(false);
  /** Código do último erro; a tela traduz com `real.errors.<code>`. */
  const error = ref<string | null>(null);
  /** Resumo da última reconciliação (null até rodar uma). */
  const lastReconcile = ref<ReconcileResult | null>(null);
  let seq = 0;

  async function load(): Promise<void> {
    const mine = ++seq;
    loading.value = true;
    try {
      const [s, c] = await Promise.all([
        getApi().links.list({ state: "Suggested", page: 1, size: SUGGESTED_SIZE }),
        getApi().links.list({ state: "Confirmed", page: 1, size: CONFIRMED_SIZE }),
      ]);
      if (mine !== seq) return;
      // Vínculos de lado vazio também aparecem (uma linha só); nada é filtrado.
      suggested.value = s.items;
      confirmed.value = c.items;
      error.value = null;
    } catch (e) {
      if (mine === seq) error.value = linkErrorCodeOf(e);
    } finally {
      if (mine === seq) loading.value = false;
    }
  }

  async function refreshPending(): Promise<void> {
    const t = useTransactionsStore();
    await Promise.all([t.load(), t.loadPendingCount()]);
  }

  async function act<T>(fn: () => Promise<T>): Promise<{ ok: boolean; value: T | null }> {
    if (busy.value) return { ok: false, value: null };
    busy.value = true;
    error.value = null;
    let value: T | null = null;
    let ok = true;
    let code: string | null = null;
    try {
      value = await fn();
    } catch (e) {
      ok = false;
      code = linkErrorCodeOf(e);
    }
    try {
      await Promise.all([load(), refreshPending()]);
    } finally {
      busy.value = false;
    }
    if (code) error.value = code; // load() zera/atribui o erro; o da ação prevalece
    return { ok, value };
  }

  const confirm = async (id: string) => (await act(() => getApi().links.confirm(id))).ok;
  const reject = async (id: string) => (await act(() => getApi().links.reject(id))).ok;
  const undo = async (id: string) => (await act(() => getApi().links.undo(id))).ok;

  /** Roda a reconciliação das pendências; devolve o resumo (ou null em erro) e recarrega vínculos e pendentes. */
  async function reconcile(): Promise<ReconcileResult | null> {
    const r = await act(() => getApi().links.reconcile());
    if (r.value) lastReconcile.value = r.value;
    return r.value;
  }

  return { suggested, confirmed, loading, busy, error, lastReconcile, load, confirm, reject, undo, reconcile };
});
