import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import { fakeOverviewApi, sum } from "../testingSummary";
import { setAppConfig } from "../appConfig";
import { addMonths, currentMonthLocal, useSummaryStore } from "./summary";

beforeEach(() => {
  setActivePinia(createPinia());
  vi.useFakeTimers();
  vi.setSystemTime(new Date(2026, 10, 3, 23, 30)); // 03/11/2026 noite, hora local
});
afterEach(() => {
  vi.useRealTimers();
  setAppConfig(null);
});

describe("month math", () => {
  it("addMonths_rolls_over_years_with_string_arithmetic", () => {
    expect(addMonths("2026-01", -1)).toBe("2025-12");
    expect(addMonths("2025-12", 1)).toBe("2026-01");
    expect(addMonths("2026-03", -5)).toBe("2025-10");
    expect(addMonths("2026-11", -13)).toBe("2025-10");
  });
  it("currentMonthLocal_uses_local_date", () => {
    expect(currentMonthLocal()).toBe("2026-11");
    expect(currentMonthLocal(new Date(2026, 0, 31, 23, 59))).toBe("2026-01");
  });
});

describe("summary store", () => {
  it("Six_parallel_calls_with_partial_failure_shows_gap_and_retry", async () => {
    let fail = true;
    const api = fakeOverviewApi({
      summaryImpl: async (m) => {
        if (m === "2026-08" && fail) throw new ApiError(500, "x");
        return sum(m);
      },
    });
    const s = useSummaryStore();
    await s.loadAll();
    expect(api.summary.month).toHaveBeenCalledTimes(6);
    expect(api.summary.month.mock.calls.map((c) => c[0]).sort()).toEqual([
      "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11",
    ]);
    expect(s.history.map((h) => h.month)).toEqual(["2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11"]);
    const gap = s.history.find((h) => h.month === "2026-08")!;
    expect(gap.summary).toBeNull();
    expect(gap.error).toBe("server");
    expect(s.history.filter((h) => h.summary).length).toBe(5);
    expect(s.current?.month).toBe("2026-11"); // reaproveitado, sem segunda chamada
    fail = false;
    await s.retryMonth("2026-08");
    expect(api.summary.month).toHaveBeenCalledTimes(7);
    expect(s.history.find((h) => h.month === "2026-08")!.summary).not.toBeNull();
    expect(s.history.find((h) => h.month === "2026-08")!.error).toBeUndefined();
  });

  it("Month_navigation_stops_at_current_month", async () => {
    fakeOverviewApi();
    const s = useSummaryStore();
    expect(s.month).toBe("2026-11");
    expect(s.canGoNext).toBe(false);
    await s.next();
    expect(s.month).toBe("2026-11");
    await s.prev();
    expect(s.month).toBe("2026-10");
    expect(s.canGoNext).toBe(true);
    await s.next();
    expect(s.month).toBe("2026-11");
    await s.setMonth("2027-02"); // futuro é recusado
    expect(s.month).toBe("2026-11");
  });

  it("late_response_from_previous_month_cannot_overwrite", async () => {
    const resolvers: Record<string, () => void> = {};
    fakeOverviewApi({
      summaryImpl: (m) =>
        m === "2026-11"
          ? new Promise((res) => (resolvers[m] = () => res(sum(m, { income: "1.00" })))) // 1º carregamento: lento
          : Promise.resolve(sum(m, { income: "2.00" })),
    });
    const s = useSummaryStore();
    const first = s.loadAll();
    const second = s.prev(); // mês 2026-10; pede 2026-05..2026-10
    await second;
    resolvers["2026-11"]();
    await first;
    expect(s.month).toBe("2026-10");
    expect(s.current?.month).toBe("2026-10");
    expect(s.history.at(-1)?.month).toBe("2026-10");
  });

  it("retry_of_stale_month_after_navigation_is_ignored", async () => {
    const api = fakeOverviewApi();
    const s = useSummaryStore();
    await s.loadAll();
    await s.prev();
    const before = JSON.stringify(s.history);
    await s.retryMonth("2026-01"); // fora da janela atual
    expect(JSON.stringify(s.history)).toBe(before);
    expect(api.summary.month).toHaveBeenCalledTimes(12);
  });
});

describe("summary store with fixed clock", () => {
  it("fixed_clock_sets_initial_month_and_blocks_next", async () => {
    vi.setSystemTime(new Date(2027, 2, 1, 10));
    setAppConfig({ fixedToday: "2026-10-15" });
    const api = fakeOverviewApi({});
    const s = useSummaryStore();
    expect(s.month).toBe("2026-10");
    expect(s.canGoNext).toBe(false);
    await s.setMonth("2026-11");
    expect(api.summary.month).not.toHaveBeenCalled();
    expect(s.month).toBe("2026-10");
  });
  it("fixed_clock_in_the_future_of_system_time_still_rules", async () => {
    vi.setSystemTime(new Date(2026, 0, 1, 10));
    setAppConfig({ fixedToday: "2026-10-15" });
    const api = fakeOverviewApi({});
    const s = useSummaryStore();
    expect(s.month).toBe("2026-10");
    await s.setMonth("2026-09");
    expect(s.month).toBe("2026-09");
    expect(api.summary.month).toHaveBeenCalled();
  });
});
