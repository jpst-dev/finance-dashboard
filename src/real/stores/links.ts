import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import { ApiError } from "../api/errors";
import type { LinkResponse, PairedLink } from "../api/types";
import { errorCodeOf } from "./errorCode";
import { useTransactionsStore } from "./transactions";

/** Quantidade de pares recentes mostrados em "Vinculados". */
export const CONFIRMED_SIZE = 10;
const SUGGESTED_SIZE = 50;

function isPaired(l: LinkResponse): l is PairedLink {
  return l.out !== null && l.in !== null;
}

/** 409 = o vínculo mudou de estado (ou uma linha já tem outro vínculo): recarregar e avisar. */
function linkErrorCode(e: unknown): string {
  if (e instanceof ApiError && e.status === 409) return "link_conflict";
  return errorCodeOf(e);
}

export const useLinksStore = defineStore("fd-links", () => {
  const suggested = ref<PairedLink[]>([]);
  const confirmed = ref<PairedLink[]>([]);
  const loading = ref(false);
  const busy = ref(false);
  /** Código do último erro; a tela traduz com `real.errors.<code>`. */
  const error = ref<string | null>(null);
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
      suggested.value = s.items.filter(isPaired);
      confirmed.value = c.items.filter(isPaired);
      error.value = null;
    } catch (e) {
      if (mine === seq) error.value = linkErrorCode(e);
    } finally {
      if (mine === seq) loading.value = false;
    }
  }

  async function refreshPending(): Promise<void> {
    const t = useTransactionsStore();
    await Promise.all([t.load(), t.loadPendingCount()]);
  }

  async function act(fn: () => Promise<unknown>): Promise<boolean> {
    if (busy.value) return false;
    busy.value = true;
    error.value = null;
    let ok = true;
    let code: string | null = null;
    try {
      await fn();
    } catch (e) {
      ok = false;
      code = linkErrorCode(e);
    }
    try {
      await Promise.all([load(), refreshPending()]);
    } finally {
      busy.value = false;
    }
    if (code) error.value = code; // load() zera/atribui o erro; o da ação prevalece
    return ok;
  }

  const confirm = (id: string) => act(() => getApi().links.confirm(id));
  const reject = (id: string) => act(() => getApi().links.reject(id));
  const undo = (id: string) => act(() => getApi().links.undo(id));

  return { suggested, confirmed, loading, busy, error, load, confirm, reject, undo };
});
