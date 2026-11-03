import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import type { InstallmentAccount, Installments } from "../api/types";
import { errorCodeOf } from "./errorCode";
import { addMonths } from "./summary";

export const useInstallmentsStore = defineStore("fd-installments", () => {
  const data = ref<Installments | null>(null);
  const loading = ref(false);
  /** Código do último erro; a tela traduz com `real.errors.<code>`. */
  const error = ref<string | null>(null);
  let seq = 0;

  async function load(): Promise<void> {
    const mine = ++seq;
    loading.value = true;
    try {
      const r = await getApi().installments.get();
      if (mine !== seq) return;
      data.value = r;
      error.value = null;
    } catch (e) {
      if (mine === seq) error.value = errorCodeOf(e);
    } finally {
      if (mine === seq) loading.value = false;
    }
  }

  return { data, loading, error, load };
});

/** Soma textual (BigInt) de strings decimais; o dinheiro nunca vira `number`. */
function sumDecimals(values: string[]): string {
  const scale = Math.max(2, ...values.map((v) => (v.split(".")[1] ?? "").length));
  let total = 0n;
  for (const v of values) {
    const neg = v.startsWith("-");
    const [i, f = ""] = (neg ? v.slice(1) : v).split(".");
    const cents = BigInt(i + f.padEnd(scale, "0"));
    total += neg ? -cents : cents;
  }
  const neg = total < 0n;
  const digits = (neg ? -total : total).toString().padStart(scale + 1, "0");
  return `${neg ? "-" : ""}${digits.slice(0, -scale)}.${digits.slice(-scale)}`;
}

/** Comprometido do mês de `today` (YYYY-MM-DD) e dos `n - 1` seguintes. `today` vem do chamador (todayISO). */
export function nextMonthsTotal(data: Installments | null, today: string, n = 3): string {
  if (!data) return "0.00";
  const start = today.slice(0, 7);
  const wanted = new Set(Array.from({ length: n }, (_, i) => addMonths(start, i)));
  return sumDecimals(data.months.filter((m) => wanted.has(m.month)).map((m) => m.total));
}

/** Contas cuja fatura-base é anterior ao mês passado: o comprometido delas pode estar defasado. */
export function staleBases(data: Installments | null, today: string): InstallmentAccount[] {
  if (!data) return [];
  const limit = addMonths(today.slice(0, 7), -1);
  return data.accounts.filter((a) => a.basedOn.slice(0, 7) < limit);
}
