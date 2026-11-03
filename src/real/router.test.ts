import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory } from "vue-router";
import { createRealRouter } from "./router";
import { useSessionStore } from "./stores/session";
import { safeNext } from "./safeNext";
import { setApi } from "./api/context";
import { fakeApi } from "./testing";

beforeEach(() => {
  setActivePinia(createPinia());
  document.title = "";
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

  it("sets_document_title_without_values", async () => {
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await useSessionStore().bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/accounts");
    expect(document.title).toBe("Contas · Finance Dashboard");
  });

  it("next_param_rejects_external_urls", () => {
    const bad = ["//evil.com", "https://x", "/\\evil.com", "javascript:alert(1)", "/a\nb", "/\t/evil", "evil", "", undefined, ["/a"]];
    for (const b of bad) expect(safeNext(b)).toBe("/");
    expect(safeNext("/transactions?page=2")).toBe("/transactions?page=2");
  });
});
