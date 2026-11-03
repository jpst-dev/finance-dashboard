import { describe, expect, it } from "vitest";
import { ApiError } from "../api/errors";
import i18n from "../i18n";
import { KNOWN_ERROR_CODES, errorCodeOf, ruleErrorCodeOf } from "./errorCode";

describe("errorCodeOf", () => {
  it("demo_read_only_code_is_kept", () => {
    expect(errorCodeOf(new ApiError(403, "demo_read_only"))).toBe("demo_read_only");
  });
  it("other_403_stays_server", () => {
    expect(errorCodeOf(new ApiError(403, "other"))).toBe("server");
    expect(errorCodeOf(new ApiError(403))).toBe("server");
  });
});

describe("ruleErrorCodeOf", () => {
  it("maps_rule_specific_codes", () => {
    expect(ruleErrorCodeOf(new ApiError(409, "duplicate_rule"))).toBe("duplicate_rule");
    expect(ruleErrorCodeOf(new ApiError(400, "invalid_pattern"))).toBe("invalid_pattern");
    expect(ruleErrorCodeOf(new ApiError(404))).toBe("not_found");
    expect(ruleErrorCodeOf(new ApiError(409))).toBe("server");
  });
  it("known_codes_have_texts_in_both_locales", () => {
    for (const loc of ["pt-BR", "en-US"] as const) {
      for (const c of KNOWN_ERROR_CODES) expect(i18n.global.te(`real.errors.${c}`, loc)).toBe(true);
    }
  });
});
