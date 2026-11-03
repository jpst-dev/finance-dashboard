/** Final do cartão para exibição (só texto, nunca atributo): "•••• 1234"; qualquer valor que não seja 4 dígitos → "". */
export function formatCardSuffix(suffix: string | null | undefined): string {
  return typeof suffix === "string" && /^\d{4}$/.test(suffix) ? `•••• ${suffix}` : "";
}
