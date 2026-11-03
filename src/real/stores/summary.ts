import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { getApi } from "../api/context";
import type { Summary, Transaction } from "../api/types";
import { todayLocalISO } from "../money/today";
import { errorCodeOf } from "./errorCode";

/** Mês local (fuso do navegador) em YYYY-MM. */
export function currentMonthLocal(d: Date = new Date()): string {
  return todayLocalISO(d).slice(0, 7);
}

/** Aritmética pura sobre "YYYY-MM" (sem Date, logo sem armadilha de fuso). */
export function addMonths(month: string, delta: number): string {
  const y = Number(month.slice(0, 4));
  const m = Number(month.slice(5, 7));
  const idx = y * 12 + (m - 1) + delta;
  const ny = Math.floor(idx / 12);
  const nm = (idx % 12) + 1;
  return `${String(ny).padStart(4, "0")}-${String(nm).padStart(2, "0")}`;
}

const MONTH_RE = /^\d{4}-(0[1-9]|1[0-2])$/;

/** Rótulo do mês a partir de uma data UTC construída das partes (não depende do fuso). */
export function monthLabel(month: string, locale: string, style: "long" | "short" = "long"): string {
  const d = new Date(Date.UTC(Number(month.slice(0, 4)), Number(month.slice(5, 7)) - 1, 1));
  const opts: Intl.DateTimeFormatOptions =
    style === "long" ? { month: "long", year: "numeric", timeZone: "UTC" } : { month: "short", timeZone: "UTC" };
  return new Intl.DateTimeFormat(locale, opts).format(d);
}

export interface HistoryEntry {
  month: string;
  summary: Summary | null;
  error?: string;
}

export const HISTORY_MONTHS = 6;

export const useSummaryStore = defineStore("fd-summary", () => {
  const month = ref(currentMonthLocal());
  const current = ref<Summary | null>(null);
  const history = ref<HistoryEntry[]>([]);
  const loading = ref(false);
  const loaded = ref(false);
  /** Código de erro do mês selecionado (a UI traduz). */
  const error = ref<string | null>(null);

  const recent = ref<Transaction[]>([]);
  const recentTotal = ref<number | null>(null);
  const recentError = ref<string | null>(null);

  const canGoNext = computed(() => month.value < currentMonthLocal());

  // Contador de requisições: respostas de uma seleção anterior são descartadas.
  let seq = 0;

  const windowOf = (m: string) => Array.from({ length: HISTORY_MONTHS }, (_, i) => addMonths(m, i - (HISTORY_MONTHS - 1)));

  async function loadAll(): Promise<void> {
    const my = ++seq;
    const m = month.value;
    loading.value = true;
    error.value = null;
    const months = windowOf(m);
    const api = getApi();
    const results = await Promise.allSettled(months.map(async (x) => api.summary.month(x)));
    if (my !== seq) return;
    history.value = results.map((r, i): HistoryEntry =>
      r.status === "fulfilled" ? { month: months[i], summary: r.value } : { month: months[i], summary: null, error: errorCodeOf(r.reason) },
    );
    const last = history.value[history.value.length - 1];
    current.value = last.summary;
    error.value = last.error ?? null;
    loading.value = false;
    loaded.value = true;
  }

  async function retryMonth(target: string): Promise<void> {
    const my = seq;
    if (!history.value.some((h) => h.month === target)) return;
    let entry: HistoryEntry;
    try {
      entry = { month: target, summary: await getApi().summary.month(target) };
    } catch (e) {
      entry = { month: target, summary: null, error: errorCodeOf(e) };
    }
    if (my !== seq) return; // a seleção mudou enquanto esperava
    const i = history.value.findIndex((h) => h.month === target);
    if (i < 0) return;
    history.value[i] = entry;
    if (target === month.value) {
      current.value = entry.summary;
      error.value = entry.error ?? null;
    }
  }

  async function setMonth(next: string): Promise<void> {
    if (!MONTH_RE.test(next) || next > currentMonthLocal()) return;
    month.value = next;
    await loadAll();
  }
  const prev = () => setMonth(addMonths(month.value, -1));
  const next = () => setMonth(addMonths(month.value, 1));

  let recentSeq = 0;
  async function loadRecent(): Promise<void> {
    const my = ++recentSeq;
    recentError.value = null;
    try {
      const r = await getApi().transactions.list({ page: 1, size: 5 });
      if (my !== recentSeq) return;
      recent.value = r.items;
      recentTotal.value = r.total;
    } catch (e) {
      if (my !== recentSeq) return;
      recentError.value = errorCodeOf(e);
    }
  }

  return {
    month, current, history, loading, loaded, error, canGoNext,
    recent, recentTotal, recentError,
    loadAll, retryMonth, setMonth, prev, next, loadRecent,
  };
});
