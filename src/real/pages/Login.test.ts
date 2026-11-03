import { describe, it, expect, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory } from "vue-router";
import i18n from "../i18n";
import { createRealRouter } from "../router";
import { setApi } from "../api/context";
import { ApiError } from "../api/errors";
import { useSessionStore } from "../stores/session";
import { fakeApi } from "../testing";
import RealApp from "../RealApp.vue";

async function setup(over: Parameters<typeof fakeApi>[0] = {}, url = "/login", signedIn = false) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const api = fakeApi(over);
  setApi(api);
  if (signedIn) await useSessionStore().bootstrap();
  const router = createRealRouter(createMemoryHistory());
  await router.push(url);
  await router.isReady();
  const w = mount(RealApp, { global: { plugins: [pinia, router, i18n] } });
  return { w, router, api };
}

const me = async () => ({ email: "user@example.test" });
/** me() só devolve usuário depois que login() resolveu (sessão criada pelo login). */
function sessionAfterLogin(loginImpl: () => Promise<void> = async () => undefined) {
  let ok = false;
  return {
    login: async () => {
      await loginImpl();
      ok = true;
    },
    me: async () => (ok ? { email: "user@example.test" } : null),
  };
}

async function fillAndSubmit(w: Awaited<ReturnType<typeof setup>>["w"], pw = "pw") {
  await w.find("#login-email").setValue("user@example.test");
  await w.find("#login-password").setValue(pw);
  await w.find("form").trigger("submit");
}

beforeEach(() => {
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
});

describe("Login", () => {
  it("has_labelled_fields", async () => {
    const { w } = await setup();
    expect(w.find("label[for=login-email]").exists()).toBe(true);
    expect(w.find("input#login-email").attributes("type")).toBe("email");
    expect(w.find("input#login-password").attributes("type")).toBe("password");
    expect(w.find("nav").exists()).toBe(false); // sem casca
  });

  it("button_disabled_during_request_and_enter_submits", async () => {
    let release!: () => void;
    let calls = 0;
    const { w } = await setup(
      sessionAfterLogin(() => {
        calls++;
        return new Promise<void>((r) => (release = r));
      }),
    );
    await fillAndSubmit(w);
    expect(calls).toBe(1);
    expect(w.find("button[type=submit]").attributes("disabled")).toBeDefined();
    await w.find("form").trigger("submit");
    expect(calls).toBe(1);
    release();
    await flushPromises();
  });

  it("success_goes_to_safe_next", async () => {
    const { w, router } = await setup(sessionAfterLogin(), "/login?next=/transactions");
    await fillAndSubmit(w);
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/transactions");
  });

  it("success_ignores_external_next", async () => {
    const { w, router } = await setup(sessionAfterLogin(), "/login?next=//evil.com");
    await fillAndSubmit(w);
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/");
  });

  it("401_shows_generic_error_and_clears_password", async () => {
    const { w } = await setup({
      login: async () => {
        throw new ApiError(401, "invalid_credentials");
      },
    });
    await fillAndSubmit(w, "wrong");
    await flushPromises();
    const alert = w.find("[role=alert]");
    expect(alert.text()).toBe("E-mail ou senha incorretos.");
    expect(alert.text()).not.toContain("user@example.test");
    expect((w.find("#login-password").element as HTMLInputElement).value).toBe("");
    expect(w.find("button[type=submit]").attributes("disabled")).toBeUndefined();
  });

  it("429_and_network_have_distinct_messages", async () => {
    const a = await setup({
      login: async () => {
        throw new ApiError(429);
      },
    });
    await fillAndSubmit(a.w);
    await flushPromises();
    expect(a.w.find("[role=alert]").text()).toContain("Muitas tentativas");
    const b = await setup({
      login: async () => {
        throw new ApiError(0, "network");
      },
    });
    await fillAndSubmit(b.w);
    await flushPromises();
    expect(b.w.find("[role=alert]").text()).toContain("Verifique a conexão");
  });
});

describe("AppShell", () => {
  it("hide_values_button_has_aria_pressed", async () => {
    const { w } = await setup({ me }, "/", true);
    const btn = w.find("[data-testid=toggle-values]");
    expect(btn.attributes("aria-pressed")).toBe("false");
    await btn.trigger("click");
    expect(btn.attributes("aria-pressed")).toBe("true");
    expect(w.find("nav[aria-label]").exists()).toBe(true);
    expect(w.find("main").exists()).toBe(true);
    expect(w.find("[data-testid=user-email]").text()).toBe("user@example.test");
  });

  it("language_toggle_switches_and_persists_locale", async () => {
    const { w } = await setup({ me }, "/", true);
    const en = w.findAll("[role=group] button").find((b) => b.text() === "EN")!;
    await en.trigger("click");
    expect(i18n.global.locale.value).toBe("en-US");
    expect(localStorage.getItem("fd.locale")).toBe("en-US");
    expect(w.text()).toContain("Sign out");
  });
});
