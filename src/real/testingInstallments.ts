import { vi } from "vitest";
import type { ApiClient } from "./api";
import { setApi } from "./api/context";
import type { InstallmentGroup, InstallmentItem, InstallmentMonth, Installments } from "./api/types";

export function item(over: Partial<InstallmentItem> = {}): InstallmentItem {
  return {
    accountId: "a1",
    description: "LOJA EXEMPLO",
    cardSuffix: "1234",
    installmentNumber: 4,
    installmentTotal: 10,
    amount: "100.00",
    ...over,
  };
}

export function month(m: string, total: string, items: InstallmentItem[] = [item()]): InstallmentMonth {
  return { month: m, total, items };
}

export function group(over: Partial<InstallmentGroup> = {}): InstallmentGroup {
  return {
    accountId: "a1",
    accountName: "Cartão Exemplo",
    cardSuffix: "1234",
    description: "LOJA EXEMPLO",
    installmentNumber: 3,
    installmentTotal: 10,
    remaining: 7,
    amount: "100.00",
    multiplicity: 1,
    endsOn: "2027-04-10",
    remainingTotal: "700.00",
    ...over,
  };
}

export function installments(over: Partial<Installments> = {}): Installments {
  return {
    totalCommitted: "700.00",
    accounts: [{ accountId: "a1", accountName: "Cartão Exemplo", basedOn: "2026-11-02" }],
    months: [month("2026-11", "100.00"), month("2026-12", "100.00"), month("2027-01", "100.00")],
    groups: [group()],
    ...over,
  };
}

export const emptyInstallments = (): Installments => ({ totalCommitted: "0.00", accounts: [], months: [], groups: [] });

export function fakeInstallmentsApi(get: () => Promise<Installments> = async () => installments()) {
  const api = { installments: { get: vi.fn(get) } };
  setApi(api as unknown as ApiClient);
  return api;
}
