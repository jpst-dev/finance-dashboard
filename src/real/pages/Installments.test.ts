import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import i18n from "../i18n";
import { ApiError } from "../api/errors";
import { setAppConfig } from "../appConfig";
import { MASK, useValuesVisibility } from "../money/visibility";
import { emptyInstallments, fakeInstallmentsApi, group, installments, month } from "../testingInstallments";
import Installments from "./Installments.vue";

const barProps = vi.hoisted(() => ({ last: null as unknown }));
vi.mock("vue-chartjs", async () => {
  const { defineComponent, h } = await import("vue");
  return {
    Bar: defineComponent({
      name: "bar",
      props: ["data", "options"],
      setup(props, { expose }) {
        expose({ chart: { update() {} } });
        return () => {
          barProps.last = props.data;
          return h("canvas");
        };
      },
    }),
  };
});

const RouterLink = { props: ["to"], template: `<a :href="typeof to === 'string' ? to : to.path"><slot /></a>` };

async function setup(data = installments(), locale: "pt-BR" | "en-US" = "pt-BR") {
  const pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.locale.value = locale;
  const api = fakeInstallmentsApi(async () => data);
  const w = mount(Installments, { global: { plugins: [pinia, i18n], stubs: { RouterLink } } });
  await flushPromises();
  return { w, api };
}

beforeEach(() => {
  localStorage.clear();
  // (visibilidade vem de uma pinia nova a cada setup)
  setAppConfig({ fixedToday: "2026-11-03" });
});
afterEach(() => {
  setAppConfig(null);
  // (visibilidade vem de uma pinia nova a cada setup)
});

describe("Installments page", () => {
  it("renders_chart_with_plot_values_and_group_list", async () => {
    const { w } = await setup();
    expect((barProps.last as { datasets: [{ data: number[] }] }).datasets[0].data).toEqual([100, 100, 100]);
    const row = w.find("[data-test=group-row]");
    expect(row.text()).toContain("LOJA EXEMPLO");
    expect(row.text()).toContain("Cartão Exemplo");
    expect(row.find("[data-test=group-progress]").text()).toBe("3 de 10");
    expect(row.find("[data-test=group-remaining]").text()).toBe("7 parcelas restantes");
    expect(row.find("[data-test=group-amount]").text()).toContain("100,00");
    expect(row.find("[data-test=group-ends]").text()).toContain("2027");
    expect(w.find("[data-test=total]").text()).toContain("700,00");
  });

  it("plural_of_remaining_in_pt_and_en", async () => {
    const mk = (r: number) => installments({ groups: [group({ remaining: r })] });
    const txt = async (r: number, l: "pt-BR" | "en-US") =>
      (await setup(mk(r), l)).w.find("[data-test=group-remaining]").text();
    expect([await txt(0, "pt-BR"), await txt(1, "pt-BR"), await txt(2, "pt-BR")]).toEqual([
      "nenhuma parcela restante",
      "1 parcela restante",
      "2 parcelas restantes",
    ]);
    expect([await txt(0, "en-US"), await txt(1, "en-US"), await txt(2, "en-US")]).toEqual([
      "no installments remaining",
      "1 installment remaining",
      "2 installments remaining",
    ]);
  });

  it("multiplicity_above_one_is_shown", async () => {
    const { w } = await setup(installments({ groups: [group({ multiplicity: 2 })] }));
    expect(w.find("[data-test=group-row]").text()).toContain("×2");
  });

  it("warns_per_account_when_base_is_older_than_last_month", async () => {
    const { w } = await setup(
      installments({
        accounts: [
          { accountId: "a1", accountName: "Cartão Velho", basedOn: "2026-08-10" },
          { accountId: "a2", accountName: "Cartão Novo", basedOn: "2026-10-10" },
        ],
      }),
    );
    const notes = w.findAll("[data-test=stale-base]");
    expect(notes).toHaveLength(1);
    expect(notes[0].text()).toBe("Cartão Velho: baseado na fatura de 08/2026; importe a mais recente.");
  });

  it("no_warning_when_base_is_recent", async () => {
    const { w } = await setup();
    expect(w.find("[data-test=stale-base]").exists()).toBe(false);
  });

  it("stale_account_without_remaining_warns_and_shows_empty_state", async () => {
    const { w } = await setup({
      ...emptyInstallments(),
      accounts: [{ accountId: "a1", accountName: "Cartão Velho", basedOn: "2026-06-10" }],
    });
    expect(w.find("[data-test=stale-base]").exists()).toBe(true);
    expect(w.find("[data-test=empty]").exists()).toBe(true);
  });

  it("empty_state_explains_and_links_to_import", async () => {
    const { w } = await setup(emptyInstallments());
    const e = w.find("[data-test=empty]");
    expect(e.text()).toContain("Importe a fatura mais recente");
    expect(e.find("a").attributes("href")).toBe("/import");
    expect(w.find("[data-test=group-row]").exists()).toBe(false);
  });

  it("error_shows_message_and_retry_reloads", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    i18n.global.locale.value = "pt-BR";
    let fail = true;
    const api = fakeInstallmentsApi(async () => {
      if (fail) throw new ApiError(500, "x");
      return installments();
    });
    const w = mount(Installments, { global: { plugins: [pinia, i18n], stubs: { RouterLink } } });
    await flushPromises();
    expect(w.find("[data-test=error]").exists()).toBe(true);
    expect(w.find("[data-test=empty]").exists()).toBe(false);
    fail = false;
    await w.find("[data-test=retry]").trigger("click");
    await flushPromises();
    expect(api.installments.get).toHaveBeenCalledTimes(2);
    expect(w.find("[data-test=group-row]").exists()).toBe(true);
  });

  it("third_party_text_is_rendered_as_text", async () => {
    const { w } = await setup(
      installments({
        groups: [group({ description: "<b>LOJA</b>", accountName: "<i>Conta</i>" })],
        accounts: [{ accountId: "a1", accountName: "<u>Velha</u>", basedOn: "2026-01-10" }],
      }),
    );
    expect(w.find("[data-test=group-row] b").exists()).toBe(false);
    expect(w.find("[data-test=group-row]").text()).toContain("<b>LOJA</b>");
    expect(w.find("[data-test=stale-base] u").exists()).toBe(false);
    expect(w.find("[data-test=stale-base]").text()).toContain("<u>Velha</u>");
  });

  it("hidden_values_mask_money_and_chart_alt", async () => {
    const { w } = await setup();
    useValuesVisibility().hidden = true;
    await flushPromises();
    expect(w.find("[data-test=group-amount]").text()).toContain(MASK);
    expect(w.find("[data-test=total]").text()).toContain(MASK);
    expect(w.find("[data-test=chart-alt]").text()).not.toMatch(/\d{2,3},\d{2}/);
  });

  it("chart_alt_lists_months_with_values_when_visible", async () => {
    const { w } = await setup(installments({ months: [month("2026-11", "123.45")] }));
    expect(w.find("[data-test=chart-alt]").text()).toContain("123,45");
  });
});
