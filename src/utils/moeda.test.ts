import { describe, it, expect } from "vitest";
import { formatarMoeda } from "./moeda";

const limpo = (s: string) => s.replace(/\s/g, " ");

describe("formatarMoeda", () => {
  it("always uses reais; the language only changes separators", () => {
    expect(limpo(formatarMoeda(20291.16, "pt-BR"))).toBe("R$ 20.291,16");
    expect(limpo(formatarMoeda(20291.16, "en-US"))).toBe("R$20,291.16");
  });
  it("accepts rounding options and treats NaN as zero", () => {
    expect(limpo(formatarMoeda(104000.4, "pt-BR", { maximumFractionDigits: 0 }))).toBe("R$ 104.000");
    expect(limpo(formatarMoeda(NaN, "pt-BR"))).toBe("R$ 0,00");
  });
});
