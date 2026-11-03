import { describe, it, expect } from "vitest";
import { formatMoney } from "./format";

const NB = " ";
describe("formatMoney", () => {
  it("formats_pt_BR", () => {
    expect(formatMoney("1234.5", "pt-BR")).toBe(`R$${NB}1.234,50`);
    expect(formatMoney("0.1", "pt-BR")).toBe(`R$${NB}0,10`);
    expect(formatMoney("10", "pt-BR")).toBe(`R$${NB}10,00`);
    expect(formatMoney("-1234.56", "pt-BR")).toBe(`-R$${NB}1.234,56`);
  });
  it("formats_en_US_with_BRL", () => {
    expect(formatMoney("1234.56", "en-US")).toBe("R$1,234.56");
    expect(formatMoney("-0.1", "en-US")).toBe("-R$0.10");
  });
  it("negative_zero_has_no_sign", () => {
    expect(formatMoney("-0.00", "pt-BR")).toBe(`R$${NB}0,00`);
    expect(formatMoney("-0", "en-US")).toBe("R$0.00");
  });
  it("keeps_every_digit_of_extreme_values", () => {
    expect(formatMoney("9999999999999999.99", "pt-BR")).toBe(`R$${NB}9.999.999.999.999.999,99`);
    expect(formatMoney("9999999999999999.99", "en-US")).toBe("R$9,999,999,999,999,999.99");
  });
  it("returns_input_untouched_when_not_a_money_string", () => {
    expect(formatMoney("abc", "pt-BR")).toBe("abc");
    expect(formatMoney("1\n", "pt-BR")).toBe("1\n");
  });
});
