import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import type { Transaction, TransactionQuery, TransactionState } from "../api/types";
import { isMoneyString } from "../money/input";
import { errorCodeOf } from "./errorCode";

export const DESCRIPTION_MAX = 300;
export const PAGE_SIZE = 50;

export type Filters = { accountId?: string; month?: string; categoryId?: string; state?: TransactionState };

export type TransactionInput = {
  date: string;
  /** String decimal já normalizada ("-12.50"); nenhuma aritmética no navegador. */
  amount: string;
  description: string;
  categoryId: string | null;
  state: TransactionState;
};

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

/** Valida o que o servidor exige; devolve o código de erro (`real.errors.<code>`) ou a descrição aparada. */
function validate(i: TransactionInput): { description: string } | string {
  if (!isMoneyString(i.amount) || /^-?0+(\.0+)?$/.test(i.amount)) return "invalid_amount";
  if (!DATE_RE.test(i.date)) return "invalid_date";
  const description = i.description.trim();
  if (description.length < 1 || description.length > DESCRIPTION_MAX) return "invalid_description";
  return { description };
}

// Loja fina: dinheiro é sempre string. O servidor ordena (data desc, id desc); a loja não reordena.
export const useTransactionsStore = defineStore("fd-transactions", () => {
  const items = ref<Transaction[]>([]);
  const page = ref(1);
  const size = ref(PAGE_SIZE);
  const total = ref(0);
  const filters = ref<Filters>({});
  const loading = ref(false);
  /** Código do último erro (ver errorCode.ts); a página traduz. Nunca texto do servidor. */
  const error = ref<string | null>(null);
  const pendingCount = ref<number | null>(null);
  const busy = ref(false);

  // Contadores de requisição: resposta atrasada de uma chamada antiga não sobrescreve a mais nova.
  let listSeq = 0;
  let pendingSeq = 0;

  function query(): TransactionQuery {
    const q: TransactionQuery = { page: page.value, size: size.value };
    const f = filters.value;
    if (f.accountId) q.accountId = f.accountId;
    if (f.month) q.month = f.month;
    if (f.categoryId) q.categoryId = f.categoryId;
    if (f.state) q.state = f.state;
    return q;
  }

  /**
   * Página fora do intervalo (itens vazios com total > 0): decisão = mostrar estado vazio, SEM erro;
   * a paginação continua utilizável (voltar). Só `remove` recua uma página quando apaga o último item.
   */
  async function load(): Promise<void> {
    const seq = ++listSeq;
    loading.value = true;
    error.value = null;
    try {
      const r = await getApi().transactions.list(query());
      if (seq !== listSeq) return;
      items.value = r.items;
      page.value = r.page;
      size.value = r.size;
      total.value = r.total;
    } catch (e) {
      if (seq !== listSeq) return;
      error.value = errorCodeOf(e);
    } finally {
      if (seq === listSeq) loading.value = false;
    }
  }

  async function loadPendingCount(): Promise<void> {
    const seq = ++pendingSeq;
    try {
      const r = await getApi().transactions.list({ state: "PendingClassification", page: 1, size: 1 });
      if (seq === pendingSeq) pendingCount.value = r.total;
    } catch {
      /* contador é auxiliar: falha silenciosa (mantém o último valor) */
    }
  }

  /** Qualquer mudança de filtro volta para a página 1. */
  function setFilters(partial: Partial<Filters>): Promise<void> {
    const next: Filters = { ...filters.value, ...partial };
    for (const k of Object.keys(next) as (keyof Filters)[]) if (!next[k]) delete next[k];
    filters.value = next;
    page.value = 1;
    return load();
  }

  /** Substitui todos os filtros (ex.: ao entrar numa tela). */
  function resetFilters(next: Filters = {}): Promise<void> {
    filters.value = {};
    return setFilters(next);
  }

  function setPage(p: number): Promise<void> {
    page.value = p;
    return load();
  }

  async function reloadAll() {
    await Promise.all([load(), loadPendingCount()]);
  }

  async function run<T>(fn: () => Promise<T>): Promise<T | null> {
    busy.value = true;
    error.value = null;
    try {
      return await fn();
    } catch (e) {
      error.value = errorCodeOf(e);
      return null;
    } finally {
      busy.value = false;
    }
  }

  async function create(input: TransactionInput & { accountId: string }): Promise<Transaction | null> {
    const v = validate(input);
    if (typeof v === "string") {
      error.value = v;
      return null;
    }
    const t = await run(() =>
      getApi().transactions.create({
        accountId: input.accountId,
        date: input.date,
        amount: input.amount,
        description: v.description,
        categoryId: input.categoryId,
        state: input.state,
      }),
    );
    if (t) await reloadAll();
    return t;
  }

  function putBody(i: TransactionInput, description: string) {
    // categoryId SEMPRE explícito: null limpa a categoria (não dependemos de "omitido" ter o mesmo efeito).
    return { date: i.date, amount: i.amount, description, categoryId: i.categoryId ?? null, state: i.state };
  }

  async function update(id: string, input: TransactionInput): Promise<Transaction | null> {
    const v = validate(input);
    if (typeof v === "string") {
      error.value = v;
      return null;
    }
    const t = await run(() => getApi().transactions.update(id, putBody(input, v.description)));
    if (t) await reloadAll();
    return t;
  }

  async function remove(id: string): Promise<boolean> {
    const ok = await run(async () => {
      await getApi().transactions.remove(id);
      return true;
    });
    if (!ok) return false;
    if (items.value.length === 1 && page.value > 1) page.value -= 1; // apagou o último item da página
    await reloadAll();
    return true;
  }

  /**
   * Reclassificação inline: atualização otimista + PUT com o corpo completo; rollback em erro.
   * Com filtro de estado ativo, a linha que deixou de combinar sai da lista no sucesso.
   */
  async function classify(id: string, patch: { state?: TransactionState; categoryId?: string | null }): Promise<boolean> {
    const idx = items.value.findIndex((x) => x.id === id);
    if (idx < 0) return false;
    const prev = items.value[idx];
    const next: Transaction = {
      ...prev,
      state: patch.state ?? prev.state,
      categoryId: patch.categoryId !== undefined ? patch.categoryId : prev.categoryId,
    };
    items.value[idx] = next;
    const saved = await run(() =>
      getApi().transactions.update(
        id,
        putBody(
          { date: prev.date, amount: prev.amount, description: prev.description, categoryId: next.categoryId, state: next.state },
          prev.description,
        ),
      ),
    );
    if (!saved) {
      const j = items.value.findIndex((x) => x.id === id);
      if (j >= 0) items.value[j] = prev; // rollback
      return false;
    }
    const f = filters.value.state;
    if (f && saved.state !== f) {
      items.value = items.value.filter((x) => x.id !== id);
    } else {
      const j = items.value.findIndex((x) => x.id === id);
      if (j >= 0) items.value[j] = saved;
    }
    await reloadAll();
    return true;
  }

  return {
    items, page, size, total, filters, loading, error, pendingCount, busy,
    load, loadPendingCount, setFilters, resetFilters, setPage, create, update, remove, classify,
  };
});
