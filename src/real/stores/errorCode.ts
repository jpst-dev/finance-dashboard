import { ApiError, DEMO_READ_ONLY_CODE, NETWORK_ERROR_CODE } from "../api/errors";

/** Códigos de erro estáveis (nunca texto do servidor); a UI traduz com `real.errors.<code>`. */
export const KNOWN_ERROR_CODES = ["network", "invalid", "not_found", "unauthorized", "rate_limit", "server", "invalid_name", "invalid_amount", "invalid_date", "demo_read_only", "link_conflict", "not_applicable", "duplicate_rule", "invalid_pattern"] as const;

export function errorCodeOf(e: unknown): string {
  if (!(e instanceof ApiError)) return "server";
  if (e.code === DEMO_READ_ONLY_CODE) return "demo_read_only";
  if (e.status === 0 || e.code === NETWORK_ERROR_CODE) return "network";
  if (e.status === 400 || e.status === 422) return "invalid";
  if (e.status === 401) return "unauthorized";
  if (e.status === 404) return "not_found";
  if (e.status === 429) return "rate_limit";
  return "server";
}

/** Ações de vínculo/marcação: 409 = o vínculo mudou; 422 `not_applicable` = a linha não cabe nessa ação. */
export function linkErrorCodeOf(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 409) return "link_conflict";
    if (e.status === 422 && e.code === "not_applicable") return "not_applicable";
  }
  return errorCodeOf(e);
}

/** Regras de categoria: 409 `duplicate_rule` e 400 `invalid_pattern` têm texto próprio; 404 vira `not_found`. */
export function ruleErrorCodeOf(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 409 && e.code === "duplicate_rule") return "duplicate_rule";
    if (e.status === 400 && e.code === "invalid_pattern") return "invalid_pattern";
  }
  return errorCodeOf(e);
}
