import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { nextTick } from "vue";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory } from "vue-router";
import { createRealRouter } from "./router";
import { useSessionStore } from "./stores/session";
import { safeNext } from "./safeNext";
import { setApi } from "./api/context";
import { setRouter } from "./routerRef";
import i18n from "./i18n";
import { fakeApi } from "./testing";

beforeEach(() => {
  setActivePinia(createPinia());
  document.title = "";
});

afterEach(() => {
  setApi(null);
  setRouter(null);
  i18n.global.locale.value = "pt-BR";
});

describe("router guard", () => {
  it("guard_blocks_anonymous", async () => {
    setApi(fakeApi());
    const router = createRealRouter(createMemoryHistory());
    await router.push("/import?x=1");
    expect(router.currentRoute.value.name).toBe("login");
    expect(router.currentRoute.value.query.next).toBe("/import?x=1");
  });

  it("allows_authenticated_and_redirects_login_to_home", async () => {
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await useSessionStore().bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/pending");
    expect(router.currentRoute.value.name).toBe("pending");
    await router.push("/login");
    expect(router.currentRoute.value.path).toBe("/");
  });

  it("rules_route_requires_auth_and_has_a_title", async () => {
    setApi(fakeApi({ me: async () => null }));
    const anon = createRealRouter(createMemoryHistory());
    await anon.push("/rules");
    expect(anon.currentRoute.value.name).toBe("login");
    setRouter(null);
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await useSessionStore().bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/rules");
    expect(router.currentRoute.value.name).toBe("rules");
    expect(document.title).toBe("Regras · Finance Dashboard");
  });

  it("installments_route_requires_auth_and_has_a_title", async () => {
    setApi(fakeApi({ me: async () => null }));
    const anon = createRealRouter(createMemoryHistory());
    await anon.push("/installments");
    expect(anon.currentRoute.value.name).toBe("login");
    setRouter(null);
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await useSessionStore().bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/installments");
    expect(router.currentRoute.value.name).toBe("installments");
    expect(document.title).toBe("Parcelas · Finance Dashboard");
  });

  it("sets_document_title_without_values", async () => {
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await useSessionStore().bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/accounts");
    expect(document.title).toBe("Contas · Finance Dashboard");
  });

  it("document_title_follows_locale_changes", async () => {
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await useSessionStore().bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/accounts");
    expect(document.title).toBe("Contas · Finance Dashboard");
    i18n.global.locale.value = "en-US";
    await nextTick();
    expect(document.title).toBe("Accounts · Finance Dashboard");
  });

  it("safeNext_rejects_overlong_values", () => {
    expect(safeNext("/" + "a".repeat(2047))).toHaveLength(2048);
    expect(safeNext("/" + "a".repeat(2048))).toBe("/");
  });

  it("next_param_rejects_external_urls", () => {
    const bad = ["//evil.com", "https://x", "/\\evil.com", "javascript:alert(1)", "/a\nb", "/\t/evil", "evil", "", undefined, ["/a"]];
    for (const b of bad) expect(safeNext(b)).toBe("/");
    expect(safeNext("/transactions?page=2")).toBe("/transactions?page=2");
  });
});
