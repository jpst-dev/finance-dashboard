import { describe, expect, it } from "vitest";
import i18n from "./index";

const t = (key: string, n: number) => i18n.global.t(key, { n }, n) as string;

describe("plurals", () => {
  it("pt-BR 0/1/many", () => {
    i18n.global.locale.value = "pt-BR";
    expect([0, 1, 2].map((n) => t("real.plural.pairs", n))).toEqual(["nenhum par vinculado", "1 par vinculado", "2 pares vinculados"]);
    expect([0, 1, 2].map((n) => t("real.plural.suggestions", n))).toEqual(["nenhuma sugestão", "1 sugestão", "2 sugestões"]);
    expect([0, 1, 2].map((n) => t("real.plural.linked", n))).toEqual(["nenhuma ligada", "1 ligada", "2 ligadas"]);
    expect([0, 1, 2].map((n) => t("real.plural.reclassified", n))).toEqual(["nenhuma reclassificada", "1 reclassificada", "2 reclassificadas"]);
  });

  it("en-US 0/1/many", () => {
    i18n.global.locale.value = "en-US";
    expect([0, 1, 2].map((n) => t("real.plural.pairs", n))).toEqual(["no pairs linked", "1 pair linked", "2 pairs linked"]);
    expect([0, 1, 2].map((n) => t("real.plural.suggestions", n))).toEqual(["no suggestions", "1 suggestion", "2 suggestions"]);
    expect([0, 1, 2].map((n) => t("real.plural.linked", n))).toEqual(["none linked", "1 linked", "2 linked"]);
    expect([0, 1, 2].map((n) => t("real.plural.reclassified", n))).toEqual(["none reclassified", "1 reclassified", "2 reclassified"]);
    i18n.global.locale.value = "pt-BR";
  });
});
