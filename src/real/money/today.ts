import { getAppConfig } from "../appConfig";

/** Data local (fuso do navegador) em YYYY-MM-DD; não usa toISOString (que é UTC e erra à noite). */
export function todayLocalISO(d: Date = new Date()): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** "Hoje" do app: a data fixa da configuração (demo) ou a data local. */
export function todayISO(): string {
  return getAppConfig().fixedToday ?? todayLocalISO();
}
