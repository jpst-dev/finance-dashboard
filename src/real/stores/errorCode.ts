import { ApiError, NETWORK_ERROR_CODE } from "../api/errors";

/** Códigos de erro estáveis (nunca texto do servidor); a UI traduz com `real.errors.<code>`. */
export const KNOWN_ERROR_CODES = ["network", "invalid", "not_found", "unauthorized", "rate_limit", "server", "invalid_name", "invalid_amount", "invalid_date"] as const;

export function errorCodeOf(e: unknown): string {
  if (!(e instanceof ApiError)) return "server";
  if (e.status === 0 || e.code === NETWORK_ERROR_CODE) return "network";
  if (e.status === 400 || e.status === 422) return "invalid";
  if (e.status === 401) return "unauthorized";
  if (e.status === 404) return "not_found";
  if (e.status === 429) return "rate_limit";
  return "server";
}
