/**
 * Erro de qualquer chamada à API.
 * - `status`: código HTTP; `0` quando a requisição nem chegou ao servidor (rede, CORS, abortada).
 * - `code`: valor de `{ error }` no corpo, quando o servidor envia (ex.: `invalid_override`, `too_large`).
 * - `field`: campo inválido, se o servidor um dia informar (hoje a API não envia).
 * A mensagem nunca inclui o corpo da resposta (pode conter valores); só status e código.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly field?: string;

  constructor(status: number, code?: string, field?: string, message?: string) {
    super(message ?? (code ? `API ${status}: ${code}` : `API ${status}`));
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.field = field;
  }
}

export const NETWORK_ERROR_CODE = "network";

/** Código do erro que o demo estático devolve em toda escrita. */
export const DEMO_READ_ONLY_CODE = "demo_read_only";
