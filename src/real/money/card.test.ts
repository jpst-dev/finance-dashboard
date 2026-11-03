import { describe, expect, it } from "vitest";
import { formatCardSuffix } from "./card";

describe("formatCardSuffix", () => {
  it("formats_four_digits", () => {
    expect(formatCardSuffix("1234")).toBe("•••• 1234");
  });
  it("rejects_other_values", () => {
    for (const v of [null, undefined, "", "123", "12345", "12a4", "1234 ", "•••• 1234"]) expect(formatCardSuffix(v)).toBe("");
  });
});
