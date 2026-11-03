/** Limites do padrão de regra (mesmos do backend). */
export const PATTERN_MIN = 3;
export const PATTERN_MAX = 100;

const DATE_TOKEN = /^\d{1,2}\/\d{1,2}(\/\d{2,4})?$/;
const NUMBER_TOKEN = /^\d+([.,]\d+)*$/;
const MASKED_DOC = (tok: string) => tok.includes("*") && /[\d.-]/.test(tok);

/**
 * Sugere o trecho de uma regra a partir da descrição ORIGINAL do lançamento (espelha o backend):
 * descarta documentos mascarados (token com `*` e dígito/ponto/hífen), datas e números finais,
 * mantém a pontuação, põe em maiúsculas sem acentos e colapsa espaços. Devolve null fora de 3 a 100 caracteres.
 */
export function suggestPattern(originalDescription: string): string | null {
  const tokens = originalDescription.split(/\s+/).filter((x) => x && !MASKED_DOC(x));
  while (tokens.length > 0) {
    const last = tokens[tokens.length - 1];
    if (DATE_TOKEN.test(last) || NUMBER_TOKEN.test(last)) tokens.pop();
    else break;
  }
  const out = tokens
    .join(" ")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toUpperCase()
    .replace(/\s+/g, " ")
    .trim();
  return out.length >= PATTERN_MIN && out.length <= PATTERN_MAX ? out : null;
}
