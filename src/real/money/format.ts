import { isMoneyString } from "./input";

const NBSP = " ";

export interface MoneyParts {
  negative: boolean; // false para zero ("-0.00" não tem sinal)
  zero: boolean;
  text: string; // valor absoluto formatado, com símbolo
}

/**
 * Formata a string decimal textualmente (sem Number, sem aritmética), então
 * nenhum dígito se perde. Moeda sempre BRL; o locale só muda a pontuação.
 */
export function moneyParts(value: string, locale: string): MoneyParts | null {
  if (!isMoneyString(value)) return null;
  const neg = value.startsWith("-");
  const [intRaw, fracRaw = ""] = (neg ? value.slice(1) : value).split(".");
  const int = intRaw.replace(/^0+(?=[0-9])/, "");
  const frac = fracRaw.padEnd(2, "0");
  const zero = /^0+$/.test(int) && frac === "00";
  const pt = locale.toLowerCase().startsWith("pt");
  const group = pt ? "." : ",";
  const dec = pt ? "," : ".";
  const grouped = int.replace(/\B(?=([0-9]{3})+$)/g, group);
  return { negative: neg && !zero, zero, text: `R$${pt ? NBSP : ""}${grouped}${dec}${frac}` };
}

export function formatMoney(value: string, locale: string): string {
  const p = moneyParts(value, locale);
  if (!p) return value;
  return (p.negative ? "-" : "") + p.text;
}
