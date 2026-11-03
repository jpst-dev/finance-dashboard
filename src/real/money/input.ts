// Mesmo padrão do contrato da API (S1a): até 16 dígitos inteiros e 2 casas.
const API_PATTERN = /^-?[0-9]{1,16}(\.[0-9]{1,2})?$/;
const GROUPED_PT = /^(-?)([0-9]{1,3}(?:\.[0-9]{3})+),([0-9]{1,2})$/;
const PLAIN = /^(-?)([0-9]+)(?:[.,]([0-9]{1,2}))?$/;

export function isMoneyString(text: string): boolean {
  return !/\n/.test(text) && API_PATTERN.test(text);
}

/**
 * Normaliza a digitação do usuário para a string decimal da API ("1234.56").
 * Aceita "1234,56", "1.234,56", "1234.56". Ambíguo ("12,345"), com espaços,
 * mais de 2 casas ou mais de 16 dígitos inteiros => null. Sem aritmética.
 */
export function parseMoneyInput(text: string): string | null {
  let sign: string, int: string, frac: string | undefined;
  const g = GROUPED_PT.exec(text);
  if (g) {
    sign = g[1];
    int = g[2].replace(/\./g, "");
    frac = g[3];
  } else {
    const p = PLAIN.exec(text);
    if (!p) return null;
    sign = p[1];
    int = p[2];
    frac = p[3];
  }
  const out = frac === undefined ? `${sign}${int}` : `${sign}${int}.${frac.padEnd(2, "0")}`;
  return isMoneyString(out) ? out : null;
}
