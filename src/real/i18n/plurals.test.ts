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
    expect([0, 1, 2].map((n) => t("real.plural.categorized", n))).toEqual(["nenhuma linha categorizada", "1 linha categorizada", "2 linhas categorizadas"]);
    expect([0, 1, 2].map((n) => t("real.import.resultCategorized", n))).toEqual(["nenhuma categorizada", "1 categorizada", "2 categorizadas"]);
    expect([0, 1, 2].map((n) => t("real.plural.installmentsRemaining", n))).toEqual(["nenhuma parcela restante", "1 parcela restante", "2 parcelas restantes"]);
  });

  it("en-US 0/1/many", () => {
    i18n.global.locale.value = "en-US";
    expect([0, 1, 2].map((n) => t("real.plural.pairs", n))).toEqual(["no pairs linked", "1 pair linked", "2 pairs linked"]);
    expect([0, 1, 2].map((n) => t("real.plural.suggestions", n))).toEqual(["no suggestions", "1 suggestion", "2 suggestions"]);
    expect([0, 1, 2].map((n) => t("real.plural.linked", n))).toEqual(["none linked", "1 linked", "2 linked"]);
    expect([0, 1, 2].map((n) => t("real.plural.reclassified", n))).toEqual(["none reclassified", "1 reclassified", "2 reclassified"]);
    expect([0, 1, 2].map((n) => t("real.plural.categorized", n))).toEqual(["no lines categorized", "1 line categorized", "2 lines categorized"]);
    expect([0, 1, 2].map((n) => t("real.import.resultCategorized", n))).toEqual(["none categorized", "1 categorized", "2 categorized"]);
    expect([0, 1, 2].map((n) => t("real.plural.installmentsRemaining", n))).toEqual(["no installments remaining", "1 installment remaining", "2 installments remaining"]);
    i18n.global.locale.value = "pt-BR";
  });
});
