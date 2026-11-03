import { describe, expect, it } from "vitest";
import { ApiError } from "../api/errors";
import { errorCodeOf } from "./errorCode";

describe("errorCodeOf", () => {
  it("demo_read_only_code_is_kept", () => {
    expect(errorCodeOf(new ApiError(403, "demo_read_only"))).toBe("demo_read_only");
  });
  it("other_403_stays_server", () => {
    expect(errorCodeOf(new ApiError(403, "other"))).toBe("server");
    expect(errorCodeOf(new ApiError(403))).toBe("server");
  });
});
