import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import Money from "./Money.vue";
import { useValuesVisibility, MASK } from "./visibility";

function make(props: Record<string, unknown>) {
  const pinia = createPinia();
  setActivePinia(pinia);
  return { w: mount(Money, { props: props as never, global: { plugins: [pinia] } }), store: useValuesVisibility() };
}
beforeEach(() => localStorage.clear());

describe("Money", () => {
  it("renders_formatted_value_when_visible", () => {
    const { w } = make({ value: "1234.56", locale: "en-US" });
    expect(w.text()).toBe("R$1,234.56");
  });
  it("hidden_leaves_no_digits_anywhere", async () => {
    const { w, store } = make({ value: "9876.54", signed: true, locale: "pt-BR" });
    store.toggle();
    await w.vm.$nextTick();
    expect(w.text()).toBe(MASK);
    const html = w.element.outerHTML.replace(/ data-v-[0-9a-f]+=""/g, "");
    expect(html).not.toMatch(/[0-9]/);
    expect(html).not.toMatch(/positive|negative|zero|\+|−/);
    expect(w.attributes("aria-label")).toBeTruthy();
    expect(w.attributes("title")).toBeUndefined();
  });
  it("signed_conveys_sign_in_text_and_class", () => {
    expect(make({ value: "5.00", signed: true, locale: "en-US" }).w.text()).toBe("+R$5.00");
    const neg = make({ value: "-5.00", signed: true, locale: "en-US" }).w;
    expect(neg.text()).toBe("−R$5.00");
    expect(neg.classes()).toContain("money-negative");
    const zero = make({ value: "-0.00", signed: true, locale: "en-US" }).w;
    expect(zero.text()).toBe("R$0.00");
    expect(zero.classes()).toContain("money-zero");
    expect(make({ value: "1.00", signed: true, locale: "en-US" }).w.classes()).toContain("money-positive");
  });
  it("unsigned_has_no_semantic_class", () => {
    const { w } = make({ value: "1.00", locale: "en-US" });
    expect(w.classes().some((c) => c.startsWith("money-"))).toBe(false);
  });
});
