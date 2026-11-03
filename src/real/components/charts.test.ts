import { beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { MASK, useValuesVisibility } from "../money/visibility";
import { sum } from "../testingSummary";
import SixMonthChart from "./SixMonthChart.vue";
import CategoryChart from "./CategoryChart.vue";

const updates = vi.hoisted(() => ({ bar: vi.fn(), doughnut: vi.fn() }));
vi.mock("vue-chartjs", async () => {
  const { defineComponent, h } = await import("vue");
  const make = (name: "bar" | "doughnut") =>
    defineComponent({
      name,
      props: ["data", "options"],
      setup(_, { expose }) {
        expose({ chart: { update: updates[name] } });
        return () => h("canvas");
      },
    });
  return { Bar: make("bar"), Doughnut: make("doughnut") };
});

beforeEach(() => {
  localStorage.clear();
  setActivePinia(createPinia());
  i18n.global.locale.value = "pt-BR";
  updates.bar.mockClear();
  updates.doughnut.mockClear();
});

const months = ["2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-11"];
const history = months.map((m) => ({
  month: m,
  summary: m === "2026-08" ? null : sum(m, { income: "1234.50", expenses: "-200.25" }),
  ...(m === "2026-08" ? { error: "server" } : {}),
}));

describe("charts", () => {
  it("Charts_mask_ticks_and_tooltips_when_hidden", async () => {
    const w = mount(SixMonthChart, { props: { history }, global: { plugins: [i18n] } });
    const bar = w.findComponent({ name: "bar" });
    const opts = bar.props("options") as any;
    const data = bar.props("data") as any;
    // valores via toPlot (number só no desenho); lacuna vira null; despesas desenhadas como magnitude
    expect(data.datasets[0].data).toEqual([1234.5, 1234.5, null, 1234.5, 1234.5, 1234.5]);
    expect(data.datasets[1].data).toEqual([200.25, 200.25, null, 200.25, 200.25, 200.25]);
    const label = opts.plugins.tooltip.callbacks.label;
    const tick = opts.scales.y.ticks.callback;
    const ctx = { parsed: { y: 1234.5 }, dataset: { label: "Receitas" } };
    expect(label(ctx)).toMatch(/1\.234,50/);
    expect(tick(1000)).toMatch(/1\.000,00/);
    useValuesVisibility().toggle();
    expect(label(ctx)).toBe(MASK);
    expect(tick(1000)).toBe(MASK);
    await flushPromises();
    expect(updates.bar).toHaveBeenCalled(); // redesenha quando "esconder" muda
    // alternativa textual e aria-label sem valores
    expect(w.html()).not.toMatch(/1\.234|1234|200,25|200\.25/);
    expect(w.find("canvas").attributes("aria-label")).not.toMatch(/[0-9]/);
  });

  it("six_month_chart_alt_text_has_formatted_values_when_visible_and_retry_buttons_for_gaps", async () => {
    const w = mount(SixMonthChart, { props: { history }, global: { plugins: [i18n] } });
    expect(w.find("[data-test=chart-alt]").text()).toMatch(/1\.234,50/);
    expect(w.find("canvas").attributes("aria-label")).toBeTruthy();
    const retry = w.findAll("[data-test=retry-month]");
    expect(retry).toHaveLength(1);
    await retry[0].trigger("click");
    expect(w.emitted("retry")![0]).toEqual(["2026-08"]);
  });

  it("Uncategorized_label_when_category_null", async () => {
    const s = sum("2026-11", {
      byCategory: [
        { categoryId: "c1", categoryName: "Mercado", total: "-300.50" },
        { categoryId: null, categoryName: null, total: "-100.00" },
        { categoryId: "c2", categoryName: "Salário", total: "900.00" },
      ],
    });
    const w = mount(CategoryChart, { props: { summary: s }, global: { plugins: [i18n] } });
    const d = w.findComponent({ name: "doughnut" }).props("data") as any;
    expect(d.labels).toEqual(["Mercado", "Sem categoria"]); // só despesas
    expect(d.datasets[0].data).toEqual([300.5, 100]);
    expect(w.text()).toContain("Sem categoria");
    i18n.global.locale.value = "en-US";
    await flushPromises();
    expect(w.text()).toContain("Uncategorized");
  });

  it("category_chart_masks_tooltip_and_updates_when_hidden", async () => {
    const w = mount(CategoryChart, { props: { summary: sum("2026-11") }, global: { plugins: [i18n] } });
    const o = w.findComponent({ name: "doughnut" }).props("options") as any;
    const label = o.plugins.tooltip.callbacks.label;
    expect(label({ parsed: 300.5, label: "Mercado" })).toMatch(/Mercado.*300,50/);
    useValuesVisibility().toggle();
    expect(label({ parsed: 300.5, label: "Mercado" })).toBe(MASK);
    await flushPromises();
    expect(updates.doughnut).toHaveBeenCalled();
    expect(w.html()).not.toMatch(/300[.,]50/);
    expect(w.find("canvas").attributes("aria-label") ?? "").not.toMatch(/[0-9]/);
  });
});
