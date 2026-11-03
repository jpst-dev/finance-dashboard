import { describe, expect, it } from "vitest";
import { suggestPattern } from "./suggestPattern";

describe("suggestPattern", () => {
  it("uppercases_strips_accents_and_collapses_spaces", () => {
    expect(suggestPattern("  Padaria   São João ")).toBe("PADARIA SAO JOAO");
  });
  it("drops_only_trailing_masked_documents", () => {
    expect(suggestPattern("PIX ENVIADO Maria ***.123.456-**")).toBe("PIX ENVIADO MARIA");
    expect(suggestPattern("PIX ENVIADO Maria •••.123.456-••")).toBe("PIX ENVIADO MARIA");
  });
  it("keeps_masked_document_in_the_middle", () => {
    expect(suggestPattern("PIX ENVIADO ***.123.456-** Maria")).toBe("PIX ENVIADO ***.123.456-** MARIA");
    expect(suggestPattern("PG *TON-LOJA EXEMPLO")).toBe("PG *TON-LOJA EXEMPLO");
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
  it("matches_backend_cross_check_vectors", () => {
    const vectors: [string, string | null][] = [
      ["PIX ENVIADO ***.123.456-** Maria", "PIX ENVIADO ***.123.456-** MARIA"],
      ["PG *TON-LOJA EXEMPLO", "PG *TON-LOJA EXEMPLO"],
      ["Pix FULANO •••.123.456-••", "PIX FULANO"],
      ["Pix FULANO ***.123.456-** 12/05", "PIX FULANO"],
      ["Transf ***.123.456-** 4567 12/05/2026", "TRANSF"],
      ["IFOOD *LOJA 123", "IFOOD *LOJA"],
      ["Pix *** Fulano", "PIX *** FULANO"],
      ["AB ***.123.456-**", null],
    ];
    for (const [description, expected] of vectors) expect(suggestPattern(description)).toBe(expected);
  });
  it("returns_null_when_too_short_or_too_long", () => {
    expect(suggestPattern("AB 12/03")).toBeNull();
    expect(suggestPattern("")).toBeNull();
    expect(suggestPattern("A".repeat(101))).toBeNull();
  });
});
