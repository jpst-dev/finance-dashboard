// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from "vitest";
import { gerarExemplo, semearSeVazio, limparDados, restaurarExemplo, CHAVES } from "./exemplo";

const hoje = new Date(2026, 9, 26); // 26 out. 2026

describe("gerarExemplo", () => {
  const { transactions, goals, investments } = gerarExemplo(hoje);

  it("covers the last 6 months, oldest first, never in the future", () => {
    const meses = new Set(transactions.map((t) => t.date.slice(0, 7)));
    expect([...meses]).toEqual(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"]);
    const datas = transactions.map((t) => t.date);
    expect(datas).toEqual([...datas].sort());
    expect(datas.every((d) => d <= "2026-10-26")).toBe(true);
  });

  it("has income and expenses every month, with unique ids", () => {
    for (const m of ["2026-05", "2026-10"]) {
      const doMes = transactions.filter((t) => t.date.startsWith(m));
      expect(doMes.some((t) => t.type === "income")).toBe(true);
      expect(doMes.some((t) => t.type === "expense")).toBe(true);
    }
    expect(new Set(transactions.map((t) => t.id)).size).toBe(transactions.length);
  });

  it("is deterministic and keeps a positive balance", () => {
    expect(gerarExemplo(hoje)).toEqual(gerarExemplo(hoje));
    const saldo = transactions.reduce((s, t) => s + (t.type === "income" ? t.amount : -t.amount), 0);
    expect(saldo).toBeGreaterThan(0);
  });

  it("creates goals in progress and crypto holdings backed by buy transactions", () => {
    expect(goals.length).toBeGreaterThanOrEqual(3);
    expect(goals.every((g) => g.currentAmount < g.targetAmount && g.deadline > "2026-10-26")).toBe(true);
    expect(investments.investments.length).toBeGreaterThanOrEqual(3);
    for (const inv of investments.investments) {
      const compras = investments.transactions.filter((t) => t.symbol === inv.symbol && t.type === "buy");
      expect(compras.reduce((s, t) => s + t.amount, 0)).toBeCloseTo(inv.amount);
      expect(inv.currentPrice).toBeGreaterThan(0);
    }
  });
});

describe("storage", () => {
  beforeEach(() => localStorage.clear());

  it("seeds an empty browser once", () => {
    expect(semearSeVazio(localStorage, hoje)).toBe(true);
    expect(JSON.parse(localStorage.getItem(CHAVES.transacoes)!).length).toBeGreaterThan(10);
    expect(localStorage.getItem(CHAVES.metas)).not.toBeNull();
    expect(localStorage.getItem(CHAVES.investimentos)).not.toBeNull();
  });

  it("never overwrites what the user already has", () => {
    localStorage.setItem(CHAVES.transacoes, "[]");
    expect(semearSeVazio(localStorage, hoje)).toBe(false);
    expect(localStorage.getItem(CHAVES.transacoes)).toBe("[]");
  });

  it("clearing removes everything and stops the example from coming back", () => {
    semearSeVazio(localStorage, hoje);
    limparDados(localStorage);
    expect(localStorage.getItem(CHAVES.transacoes)).toBeNull();
    expect(localStorage.getItem(CHAVES.metas)).toBeNull();
    expect(localStorage.getItem(CHAVES.investimentos)).toBeNull();
    expect(semearSeVazio(localStorage, hoje)).toBe(false);
  });

  it("restoring brings the example back", () => {
    limparDados(localStorage);
    restaurarExemplo(localStorage, hoje);
    expect(JSON.parse(localStorage.getItem(CHAVES.transacoes)!).length).toBeGreaterThan(10);
  });
});

describe("versions", () => {
  beforeEach(() => localStorage.clear());

  it("replaces an older sample (dollars) with the current one", () => {
    localStorage.setItem(CHAVES.transacoes, JSON.stringify([{ id: "exemplo-velho", amount: 5200 }]));
    expect(semearSeVazio(localStorage, hoje)).toBe(true);
    const salario = JSON.parse(localStorage.getItem(CHAVES.transacoes)!).find((t: { category: string }) => t.category === "Salary");
    expect(salario.amount).toBe(8400);
  });

  it("keeps an older sample the user has edited", () => {
    localStorage.setItem(CHAVES.transacoes, JSON.stringify([{ id: "exemplo-velho" }, { id: "1761500000000" }]));
    expect(semearSeVazio(localStorage, hoje)).toBe(false);
  });
});
