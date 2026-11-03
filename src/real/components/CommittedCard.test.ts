import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { setAppConfig } from "../appConfig";
import { MASK, useValuesVisibility } from "../money/visibility";
import { emptyInstallments, fakeInstallmentsApi, installments, month } from "../testingInstallments";
import { useInstallmentsStore } from "../stores/installments";
import CommittedCard from "./CommittedCard.vue";

const RouterLink = { props: ["to"], template: `<a :href="typeof to === 'string' ? to : to.path"><slot /></a>` };

async function setup(data = installments()) {
  const pinia = createPinia();
  setActivePinia(pinia);
  fakeInstallmentsApi(async () => data);
  await useInstallmentsStore().load();
  const w = mount(CommittedCard, { global: { plugins: [pinia, i18n], stubs: { RouterLink } } });
  await flushPromises();
  return w;
}

beforeEach(() => {
  localStorage.clear();
  i18n.global.locale.value = "pt-BR";
  // (visibilidade vem de uma pinia nova a cada setup)
  setAppConfig({ fixedToday: "2026-11-03" });
});
afterEach(() => {
  setAppConfig(null);
  // (visibilidade vem de uma pinia nova a cada setup)
});

describe("CommittedCard", () => {
  it("shows_sum_of_current_and_next_two_months_with_link", async () => {
    const w = await setup(
      installments({
        months: [month("2026-10", "9.00"), month("2026-11", "100.10"), month("2026-12", "200.20"), month("2027-01", "0.30"), month("2027-02", "50.00")],
      }),
    );
    const c = w.find("[data-test=committed-card]");
    expect(c.text()).toContain("Comprometido nos próximos meses");
    expect(c.find("[data-test=committed-total]").text()).toContain("300,60");
    expect(c.find("a").attributes("href")).toBe("/installments");
  });
  it("hidden_when_total_is_zero", async () => {
    expect((await setup(emptyInstallments())).find("[data-test=committed-card]").exists()).toBe(false);
    const far = await setup(installments({ months: [month("2027-06", "50.00")] }));
    expect(far.find("[data-test=committed-card]").exists()).toBe(false);
  });
  it("hidden_before_data_loads", () => {
    setActivePinia(createPinia());
    const w = mount(CommittedCard, { global: { plugins: [i18n], stubs: { RouterLink } } });
    expect(w.find("[data-test=committed-card]").exists()).toBe(false);
  });
  it("respects_hidden_values", async () => {
    const w = await setup();
    useValuesVisibility().hidden = true;
    await flushPromises();
    expect(w.find("[data-test=committed-total]").text()).toContain(MASK);
  });
});
