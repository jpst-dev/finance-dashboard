import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { getApi } from "../api/context";
import type { Account, AccountType, Checkpoint } from "../api/types";
import { errorCodeOf } from "./errorCode";

export const NAME_MAX = 80;

export type NewAccountInput = {
  name: string;
  type: AccountType;
  institution?: string;
  externalIdentifier?: string;
};

/** Nome aparado, 1–80 caracteres; `null` se inválido. */
export function validName(raw: string): string | null {
  const n = raw.trim();
  return n.length >= 1 && n.length <= NAME_MAX ? n : null;
}

// Loja fina: sem aritmética de dinheiro; saldos vêm do servidor como string.
export const useAccountsStore = defineStore("fd-accounts", () => {
  const items = ref<Account[]>([]);
  const loaded = ref(false);
  const busy = ref(false);
  /** Código do último erro (ver errorCode.ts); a página traduz. */
  const error = ref<string | null>(null);

  const active = computed(() => items.value.filter((a) => !a.isArchived));
  const archived = computed(() => items.value.filter((a) => a.isArchived));

  async function run<T>(fn: () => Promise<T>): Promise<T | null> {
    // Reentrância (ex.: Enter repetido durante um PUT em andamento): ignora a segunda chamada.
    if (busy.value) return null;
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

  function replace(a: Account) {
    const i = items.value.findIndex((x) => x.id === a.id);
    if (i >= 0) items.value[i] = a;
    else items.value.push(a);
  }

  async function load(): Promise<boolean> {
    const r = await run(() => getApi().accounts.list());
    if (r) {
      items.value = r;
      loaded.value = true;
    }
    return r !== null;
  }

  async function create(input: NewAccountInput): Promise<Account | null> {
    const name = validName(input.name);
    if (!name) {
      error.value = "invalid_name";
      return null;
    }
    const a = await run(() =>
      getApi().accounts.create({
        name,
        type: input.type,
        institution: input.institution?.trim() || null,
        externalIdentifier: input.externalIdentifier?.trim() || null,
      }),
    );
    if (a) replace(a);
    return a;
  }

  /** PUT exige o nome; reenvia os demais campos atuais para não apagá-los. */
  async function update(id: string, patch: { name?: string; isArchived?: boolean }): Promise<Account | null> {
    const cur = items.value.find((x) => x.id === id);
    if (!cur) {
      error.value = "not_found";
      return null;
    }
    const name = validName(patch.name ?? cur.name);
    if (!name) {
      error.value = "invalid_name";
      return null;
    }
    const a = await run(() =>
      getApi().accounts.update(id, {
        name,
        type: cur.type,
        institution: cur.institution,
        externalIdentifier: cur.externalIdentifier,
        isArchived: patch.isArchived ?? cur.isArchived,
      }),
    );
    if (a) replace(a);
    return a;
  }

  const rename = (id: string, name: string) => update(id, { name });
  const setArchived = (id: string, isArchived: boolean) => update(id, { isArchived });

  /** `amount` já normalizado (string decimal); recarrega a lista para refletir o novo saldo. */
  async function addCheckpoint(id: string, date: string, amount: string): Promise<Checkpoint | null> {
    const c = await run(() => getApi().accounts.addCheckpoint(id, { date, amount }));
    if (c) await refreshBalances();
    return c;
  }

  async function refreshBalances() {
    try {
      items.value = await getApi().accounts.list();
    } catch (e) {
      error.value = errorCodeOf(e);
    }
  }

  return { items, loaded, busy, error, active, archived, load, create, rename, setArchived, addCheckpoint };
});
