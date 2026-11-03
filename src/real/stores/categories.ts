import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import type { Category } from "../api/types";
import { errorCodeOf } from "./errorCode";

export const useCategoriesStore = defineStore("fd-categories", () => {
  const items = ref<Category[]>([]);
  const loaded = ref(false);
  const busy = ref(false);
  const error = ref<string | null>(null);

  async function load(): Promise<boolean> {
    busy.value = true;
    error.value = null;
    try {
      items.value = await getApi().categories.list();
      loaded.value = true;
      return true;
    } catch (e) {
      error.value = errorCodeOf(e);
      return false;
    } finally {
      busy.value = false;
    }
  }

  /**
   * Categorias aplicáveis ao valor, decididas SÓ pelo sinal do texto (sem converter para número):
   * começa com "-" => Despesa; texto de dinheiro válido positivo => Receita.
   * Zero ("0.00", "-0.00"), vazio ou inválido ("abc", "+5") => todas (padrão sensato: ainda não dá para decidir).
   */
  function forAmount(amount: string): Category[] {
    const m = /^(-?)([0-9]{1,16})(?:\.([0-9]{1,2}))?$/.exec(amount);
    if (!m) return items.value;
    if (/^0*$/.test(m[2] + (m[3] ?? ""))) return items.value;
    const kind = m[1] === "-" ? "Expense" : "Income";
    return items.value.filter((c) => c.kind === kind);
  }

  return { items, loaded, busy, error, load, forAmount };
});
