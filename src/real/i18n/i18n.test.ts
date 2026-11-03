import { describe, it, expect } from "vitest";
import pt from "./pt-BR.json";
import en from "./en-US.json";

function keys(o: unknown, prefix = ""): string[] {
  if (o === null || typeof o !== "object") return [prefix];
  return Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => keys(v, prefix ? `${prefix}.${k}` : k));
}

describe("real i18n", () => {
  it("pt-BR_and_en-US_have_identical_key_sets", () => {
    expect(keys(en).sort()).toEqual(keys(pt).sort());
  });
});
