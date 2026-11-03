import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useValuesVisibility, MASK } from "./visibility";

beforeEach(() => {
  localStorage.clear();
  setActivePinia(createPinia());
});

describe("useValuesVisibility", () => {
  it("defaults_visible_and_toggles_and_persists", () => {
    const s = useValuesVisibility();
    expect(s.hidden).toBe(false);
    s.toggle();
    expect(s.hidden).toBe(true);
    expect(localStorage.getItem("fd.hideValues")).toBe("1");
    setActivePinia(createPinia());
    expect(useValuesVisibility().hidden).toBe(true);
    useValuesVisibility().toggle();
    expect(localStorage.getItem("fd.hideValues")).toBe("0");
  });
  it("works_when_storage_throws", () => {
    const g = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("x");
    });
    const p = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("x");
    });
    setActivePinia(createPinia());
    const s = useValuesVisibility();
    expect(s.hidden).toBe(false);
    expect(() => s.toggle()).not.toThrow();
    expect(s.hidden).toBe(true);
    g.mockRestore();
    p.mockRestore();
  });
  it("mask_is_stable", () => {
    expect(MASK).toBe("••••");
  });
});
