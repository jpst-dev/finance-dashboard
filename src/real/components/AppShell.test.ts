import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createMemoryHistory } from "vue-router";
import i18n from "../i18n";
import { setApi } from "../api/context";
import type { ApiClient } from "../api";
import { APP_CONFIG_KEY, setAppConfig, type AppConfig } from "../appConfig";
import { useSessionStore } from "../stores/session";
import { fakeApi } from "../testing";
import AppShell from "./AppShell.vue";

async function setup(config?: AppConfig) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const base = fakeApi();
  setApi({ ...base, transactions: { list: vi.fn(async () => ({ items: [], total: 0, page: 1, size: 1 })) } } as unknown as ApiClient);
  useSessionStore().user = { email: "a@exemplo.test" };
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: "/", component: { template: "<div/>" } }] });
  await router.push("/");
  const w = mount(AppShell, {
    global: { plugins: [pinia, i18n, router], provide: config ? { [APP_CONFIG_KEY as symbol]: config } : {} },
  });
  await flushPromises();
  return w;
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
});
afterEach(() => setAppConfig(null));

describe("AppShell", () => {
  it("real_defaults_show_logout_and_no_banner", async () => {
    const w = await setup();
    expect(w.find("[data-testid=logout]").exists()).toBe(true);
    expect(w.find("[data-testid=demo-banner]").exists()).toBe(false);
  });
  it("menu_has_rules_link", async () => {
    const w = await setup();
    const link = w.findAll("nav a").find((a) => a.attributes("href") === "/rules");
    expect(link?.text()).toBe("Regras");
  });
  it("read_only_config_shows_banner_and_hides_logout", async () => {
    const w = await setup({ readOnly: true, hideLogout: true, fixedToday: null });
    expect(w.find("[data-testid=demo-banner]").text()).toBe("Demonstração com dados fictícios, somente leitura");
    expect(w.find("[data-testid=logout]").exists()).toBe(false);
  });
});
