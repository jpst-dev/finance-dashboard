import { describe, it, expect } from "vitest";
import { parseMoneyInput, isMoneyString } from "./input";

describe("parseMoneyInput", () => {
  it("accepts_common_forms", () => {
    expect(parseMoneyInput("1234,56")).toBe("1234.56");
    expect(parseMoneyInput("1.234,56")).toBe("1234.56");
    expect(parseMoneyInput("1234.56")).toBe("1234.56");
    expect(parseMoneyInput("0,1")).toBe("0.10");
    expect(parseMoneyInput("-12,5")).toBe("-12.50");
    expect(parseMoneyInput("1234")).toBe("1234");
  });
  it("rejects_invalid", () => {
    for (const t of ["", "abc", "12,345", "1.234", "1,234.56", "1,2,3", "1,234", "12.345", "-", "--1", "1e3", ",5", "5,"]) {
      expect(parseMoneyInput(t), t).toBeNull();
    }
  });
  it("rejects_whitespace_and_newlines", () => {
    for (const t of [" 1,00", "1,00 ", "1,00\n", "\n1,00", "1 000,00"]) {
      expect(parseMoneyInput(t), JSON.stringify(t)).toBeNull();
    }
  });
  it("limits_integer_digits_to_16", () => {
    expect(parseMoneyInput("9999999999999999,99")).toBe("9999999999999999.99");
    expect(parseMoneyInput("10000000000000000")).toBeNull();
  });
});

describe("isMoneyString", () => {
  it("matches_api_pattern", () => {
    expect(isMoneyString("1234.56")).toBe(true);
    expect(isMoneyString("-0.1")).toBe(true);
    expect(isMoneyString("1234,56")).toBe(false);
    expect(isMoneyString("1.234")).toBe(false);
    expect(isMoneyString("")).toBe(false);
    expect(isMoneyString("12345678901234567")).toBe(false);
  });
  it("rejects_trailing_newline", () => {
    expect(isMoneyString("1.00\n")).toBe(false);
    expect(isMoneyString(" 1.00")).toBe(false);
  });
});
