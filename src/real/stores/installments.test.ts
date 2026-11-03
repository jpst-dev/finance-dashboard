import { beforeEach, describe, expect, it } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import { fakeInstallmentsApi, installments, month } from "../testingInstallments";
import { nextMonthsTotal, staleBases, useInstallmentsStore } from "./installments";

beforeEach(() => setActivePinia(createPinia()));

describe("installments store", () => {
  it("load_fills_data_and_clears_loading", async () => {
    fakeInstallmentsApi();
    const s = useInstallmentsStore();
    const p = s.load();
    expect(s.loading).toBe(true);
    await p;
    expect(s.loading).toBe(false);
    expect(s.data?.totalCommitted).toBe("700.00");
    expect(s.error).toBeNull();
  });
  it("load_failure_sets_error_code_and_keeps_data_null", async () => {
    fakeInstallmentsApi(async () => {
      throw new ApiError(500, "x");
    });
    const s = useInstallmentsStore();
    await s.load();
    expect(s.error).toBe("server");
    expect(s.data).toBeNull();
    expect(s.loading).toBe(false);
  });
  it("later_load_wins_over_stale_one", async () => {
    let n = 0;
    const resolvers: Array<() => void> = [];
    fakeInstallmentsApi(
      () =>
        new Promise((res) => {
          const mine = ++n;
          resolvers.push(() => res(installments({ totalCommitted: mine === 1 ? "1.00" : "2.00" })));
        }),
    );
    const s = useInstallmentsStore();
    const a = s.load();
    const b = s.load();
    resolvers[1]();
    await b;
    resolvers[0]();
    await a;
    expect(s.data?.totalCommitted).toBe("2.00");
  });
});

describe("nextMonthsTotal", () => {
  const data = installments({
    months: [
      month("2026-10", "5.00"),
      month("2026-11", "100.10"),
      month("2026-12", "200.20"),
      month("2027-01", "0.30"),
      month("2027-02", "9.99"),
    ],
  });
  it("sums_current_month_plus_two_as_decimal_string", () => {
    expect(nextMonthsTotal(data, "2026-11-03")).toBe("300.60");
  });
  it("crosses_year_boundary", () => {
    expect(nextMonthsTotal(data, "2026-12-31")).toBe("210.49");
  });
  it("month_edges_use_first_and_last_day", () => {
    expect(nextMonthsTotal(data, "2026-11-01")).toBe("300.60");
    expect(nextMonthsTotal(data, "2026-11-30")).toBe("300.60");
  });
  it("does_not_lose_precision_on_large_values", () => {
    const big = installments({ months: [month("2026-11", "9007199254740993.01"), month("2026-12", "0.02")] });
    expect(nextMonthsTotal(big, "2026-11-03")).toBe("9007199254740993.03");
  });
  it("respects_n_and_returns_zero_for_none", () => {
    expect(nextMonthsTotal(data, "2026-11-03", 1)).toBe("100.10");
    expect(nextMonthsTotal(data, "2030-01-01")).toBe("0.00");
    expect(nextMonthsTotal(null, "2026-11-03")).toBe("0.00");
  });
});

describe("staleBases", () => {
  const acc = (basedOn: string) => ({ accountId: "a", accountName: "C", basedOn });
  it("flags_bases_from_a_month_before_the_current_one", () => {
    const d = installments({ accounts: [acc("2026-10-10"), acc("2026-09-10"), acc("2026-11-02")] });
    expect(staleBases(d, "2026-11-03").map((a) => a.basedOn)).toEqual(["2026-10-10", "2026-09-10"]);
  });
  it("base_in_the_current_month_does_not_warn_even_on_day_one", () => {
    const d = installments({ accounts: [acc("2026-11-30")] });
    expect(staleBases(d, "2026-11-01")).toEqual([]);
  });
  it("january_flags_december", () => {
    const d = installments({ accounts: [acc("2025-12-31"), acc("2026-01-02")] });
    expect(staleBases(d, "2026-01-05").map((a) => a.basedOn)).toEqual(["2025-12-31"]);
  });
});
