import { describe, expect, it } from "vitest";
import { suggestPattern } from "./suggestPattern";

describe("suggestPattern", () => {
  it("uppercases_strips_accents_and_collapses_spaces", () => {
    expect(suggestPattern("  Padaria   São João ")).toBe("PADARIA SAO JOAO");
  });
  it("drops_masked_documents", () => {
    expect(suggestPattern("PIX ENVIADO ***.123.456-** Maria")).toBe("PIX ENVIADO MARIA");
  });
  it("drops_trailing_dates_and_numbers", () => {
    expect(suggestPattern("SUPERMERCADO EXEMPLO 12/03 4567")).toBe("SUPERMERCADO EXEMPLO");
    expect(suggestPattern("SUPERMERCADO EXEMPLO 12/03/2026")).toBe("SUPERMERCADO EXEMPLO");
  });
  it("keeps_inner_numbers_and_punctuation", () => {
    expect(suggestPattern("Loja 24H - Centro")).toBe("LOJA 24H - CENTRO");
  });
  it("keeps_asterisk_store_marker", () => {
    expect(suggestPattern("IFOOD *LOJA")).toBe("IFOOD *LOJA");
  });
  it("matches_backend_vectors", () => {
    expect(suggestPattern("Pix FULANO FICTICIO ***.123.456-** 12/05")).toBe("PIX FULANO FICTICIO");
    expect(suggestPattern("Compra Supermercado Exemplo 12/05/2026")).toBe("COMPRA SUPERMERCADO EXEMPLO");
    expect(suggestPattern("IFOOD *LOJA EXEMPLO")).toBe("IFOOD *LOJA EXEMPLO");
    expect(suggestPattern("Padaria 24 Horas")).toBe("PADARIA 24 HORAS");
    expect(suggestPattern("AB 12")).toBeNull();
  });
  it("returns_null_when_too_short_or_too_long", () => {
    expect(suggestPattern("AB 12/03")).toBeNull();
    expect(suggestPattern("")).toBeNull();
    expect(suggestPattern("A".repeat(101))).toBeNull();
  });
});
