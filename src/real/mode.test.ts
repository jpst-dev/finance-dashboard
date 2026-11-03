import { describe, it, expect } from "vitest";
import { resolveMode } from "./mode";

describe("resolveMode", () => {
  it("defaults_to_demo_when_env_missing", () => {
    expect(resolveMode({})).toBe("demo");
    expect(resolveMode({ VITE_APP_MODE: "" })).toBe("demo");
  });
  it("real_when_env_is_real", () => {
    expect(resolveMode({ VITE_APP_MODE: "real" })).toBe("real");
  });
  it("other_values_fall_back_to_demo", () => {
    expect(resolveMode({ VITE_APP_MODE: "REAL" })).toBe("demo");
    expect(resolveMode({ VITE_APP_MODE: "prod" })).toBe("demo");
  });
});
