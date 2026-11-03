import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useValuesVisibility, MASK } from "./visibility";
import { toPlot, chartMoney, chartMoneyOptions } from "./chart";

beforeEach(() => {
  localStorage.clear();
  setActivePinia(createPinia());
});

describe("chart helpers", () => {
  it("toPlot_only_converts_never_adds", () => {
    expect(toPlot("10.20")).toBe(10.2);
    expect(toPlot("0.10")).toBe(0.1);
    expect(toPlot("-5")).toBe(-5);
  });
  it("tooltip_and_tick_format_when_visible", () => {
    expect(chartMoney.tooltip(1234.5, "en-US")).toBe("R$1,234.50");
    expect(chartMoney.tooltip("1234.50", "en-US")).toBe("R$1,234.50");
    expect(chartMoney.tick(1000, "en-US")).toBe("R$1,000.00");
  });
  it("mask_when_hidden_read_at_call_time", () => {
    const opts = chartMoneyOptions(() => "pt-BR");
    const label = opts.plugins.tooltip.callbacks.label;
    const tick = opts.scales.y.ticks.callback;
    const ctx = { parsed: { y: 12.3 }, dataset: { label: "Saldo" } };
    expect(label(ctx)).toContain("12,30");
    useValuesVisibility().toggle();
    expect(label(ctx)).toBe(MASK);
    expect(label(ctx)).not.toMatch(/[0-9]/);
    expect(tick(1500)).toBe(MASK);
    expect(chartMoney.tooltip(5, "pt-BR")).toBe(MASK);
    expect(chartMoney.tick("5", "pt-BR")).toBe(MASK);
  });
});
