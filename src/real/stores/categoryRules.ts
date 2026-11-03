import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import type { CategoryRule } from "../api/types";
import { ruleErrorCodeOf } from "./errorCode";
import { useSummaryStore } from "./summary";
import { useTransactionsStore } from "./transactions";

export interface RuleSuggestion {
  pattern: string;
  categoryId: string;
}

export const useCategoryRulesStore = defineStore("fd-category-rules", () => {
  const rules = ref<CategoryRule[]>([]);
  const loading = ref(false);
  const busy = ref(false);
  /** Código do último erro; a tela traduz com `real.errors.<code>`. */
  const error = ref<string | null>(null);
  /** Quantas linhas a última aplicação categorizou (null até aplicar). */
  const lastApplied = ref<number | null>(null);
  /** Sugestão do atalho em Lançamentos; a página Regras consome e limpa (via store, nunca pela URL). */
  const suggestion = ref<RuleSuggestion | null>(null);
  let seq = 0;

  async function load(): Promise<void> {
    const mine = ++seq;
    loading.value = true;
    try {
      const r = await getApi().categoryRules.list();
      if (mine !== seq) return;
      rules.value = r;
      error.value = null;
    } catch (e) {
      if (mine === seq) error.value = ruleErrorCodeOf(e);
    } finally {
      if (mine === seq) loading.value = false;
    }
  }

  /** Executa uma ação com trava de duplo clique; sempre recarrega as regras e preserva o erro da ação. */
  async function act<T>(fn: () => Promise<T>, afterOk?: () => Promise<unknown>): Promise<{ ok: boolean; value: T | null }> {
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
      code = ruleErrorCodeOf(e);
    }
    try {
      await Promise.all([load(), ...(ok && afterOk ? [afterOk()] : [])]);
    } finally {
      busy.value = false;
    }
    if (code) error.value = code;
    return { ok, value };
  }

  const create = async (pattern: string, categoryId: string) =>
    (await act(() => getApi().categoryRules.create({ pattern, categoryId }))).ok;
  const remove = async (id: string) => (await act(() => getApi().categoryRules.remove(id))).ok;

  /** Aplica as regras; recarrega regras, lançamentos e resumo. Devolve quantas linhas foram categorizadas (null em erro). */
  async function apply(): Promise<number | null> {
    const r = await act(
      () => getApi().categoryRules.apply(),
      async () => {
        const t = useTransactionsStore();
        const s = useSummaryStore();
        await Promise.all([t.load(), t.loadPendingCount(), s.loaded ? s.loadAll() : Promise.resolve()]);
      },
    );
    if (r.value) lastApplied.value = r.value.applied;
    return r.value ? r.value.applied : null;
  }

  return { rules, loading, busy, error, lastApplied, suggestion, load, create, remove, apply };
});
