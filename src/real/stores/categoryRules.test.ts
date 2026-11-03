import { beforeEach, describe, expect, it } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import { fakeRulesApi, rule } from "../testingRules";
import { useCategoryRulesStore } from "./categoryRules";
import { useSummaryStore } from "./summary";

beforeEach(() => setActivePinia(createPinia()));

describe("category rules store", () => {
  it("load_fills_rules", async () => {
    fakeRulesApi([rule()]);
    const s = useCategoryRulesStore();
    await s.load();
    expect(s.rules.map((r) => r.id)).toEqual(["r1"]);
  });

  it("create_success_reloads", async () => {
    const api = fakeRulesApi();
    const s = useCategoryRulesStore();
    expect(await s.create("PADARIA", "c1")).toBe(true);
    expect(api.categoryRules.create).toHaveBeenCalledWith({ pattern: "PADARIA", categoryId: "c1" });
    expect(s.rules.map((r) => r.pattern)).toEqual(["PADARIA"]);
    expect(s.error).toBeNull();
  });

  it("create_errors_are_mapped", async () => {
    const api = fakeRulesApi();
    const s = useCategoryRulesStore();
    api.categoryRules.create.mockRejectedValueOnce(new ApiError(409, "duplicate_rule"));
    expect(await s.create("X", "c1")).toBe(false);
    expect(s.error).toBe("duplicate_rule");
    api.categoryRules.create.mockRejectedValueOnce(new ApiError(400, "invalid_pattern"));
    expect(await s.create("X", "c1")).toBe(false);
    expect(s.error).toBe("invalid_pattern");
    api.categoryRules.create.mockRejectedValueOnce(new ApiError(404));
    await s.create("X", "c1");
    expect(s.error).toBe("not_found");
  });

  it("remove_reloads", async () => {
    const api = fakeRulesApi([rule()]);
    const s = useCategoryRulesStore();
    await s.load();
    expect(await s.remove("r1")).toBe(true);
    expect(api.categoryRules.remove).toHaveBeenCalledWith("r1");
    expect(s.rules).toEqual([]);
  });

  it("apply_sets_lastApplied_and_reloads_rules_and_transactions", async () => {
    const api = fakeRulesApi([rule()]);
    const s = useCategoryRulesStore();
    expect(await s.apply()).toBe(2);
    expect(s.lastApplied).toBe(2);
    expect(api.categoryRules.list).toHaveBeenCalled();
    const sizes = api.transactions.list.mock.calls.map((c) => (c[0] as { size?: number }).size);
    expect(sizes).toContain(1);
    expect(sizes.length).toBeGreaterThanOrEqual(2);
  });

  it("apply_reloads_summary_when_it_was_loaded", async () => {
    const api = fakeRulesApi();
    await useCategoryRulesStore().apply();
    expect(api.summary.month).not.toHaveBeenCalled();
    useSummaryStore().loaded = true;
    await useCategoryRulesStore().apply();
    expect(api.summary.month).toHaveBeenCalled();
  });

  it("apply_error_keeps_lastApplied_and_sets_error", async () => {
    const api = fakeRulesApi();
    api.categoryRules.apply.mockRejectedValueOnce(new ApiError(0));
    const s = useCategoryRulesStore();
    expect(await s.apply()).toBeNull();
    expect(s.error).toBe("network");
    expect(s.lastApplied).toBeNull();
  });

  it("busy_blocks_double_click", async () => {
    const api = fakeRulesApi();
    let release!: () => void;
    api.categoryRules.apply.mockImplementationOnce(() => new Promise((r) => { release = () => r({ applied: 1 }); }));
    const s = useCategoryRulesStore();
    const first = s.apply();
    expect(s.busy).toBe(true);
    expect(await s.apply()).toBeNull();
    release();
    await first;
    expect(api.categoryRules.apply).toHaveBeenCalledTimes(1);
    expect(s.busy).toBe(false);
  });
});
