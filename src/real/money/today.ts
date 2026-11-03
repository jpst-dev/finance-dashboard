/** Data local (fuso do navegador) em YYYY-MM-DD; não usa toISOString (que é UTC e erra à noite). */
export function todayLocalISO(d: Date = new Date()): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
