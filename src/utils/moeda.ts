import i18n from "@/i18n";

// O app guarda valores em reais. O idioma muda só a pontuação (R$ 1.234,56 / R$1,234.56),
// nunca a moeda: trocar o símbolo sem converter mostraria valores errados.
export const MOEDA = "BRL";

// Ler o locale do i18n dentro do render deixa o valor reativo à troca de idioma.
export const formatarMoeda = (
  valor: number,
  locale: string = i18n.global.locale.value,
  opcoes: Intl.NumberFormatOptions = {}
): string =>
  new Intl.NumberFormat(locale, { style: "currency", currency: MOEDA, ...opcoes }).format(
    Number.isNaN(valor) ? 0 : valor
  );
