import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory } from "vue-router";
import { useSessionStore } from "./session";
import { setApi } from "../api/context";
import { setRouter } from "../routerRef";
import { createRealRouter } from "../router";
import { ApiError } from "../api/errors";
import { fakeApi } from "../testing";

const tick = () => new Promise((r) => setTimeout(r, 0));

beforeEach(() => {
  setActivePinia(createPinia());
});

afterEach(() => {
  setApi(null);
  setRouter(null);
});

describe("session store", () => {
  it("bootstrap_sets_authenticated_when_me_returns_user", async () => {
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    const s = useSessionStore();
    expect(s.status).toBe("unknown");
    await s.bootstrap();
    expect(s.status).toBe("authenticated");
    expect(s.user).toEqual({ email: "user@example.test" });
  });

  it("bootstrap_sets_anonymous_when_me_is_null_or_fails", async () => {
    setApi(fakeApi());
    const s = useSessionStore();
    await s.bootstrap();
    expect(s.status).toBe("anonymous");
    setApi(
      fakeApi({
        me: async () => {
          throw new ApiError(0, "network");
        },
      }),
    );
    await s.bootstrap();
    expect(s.status).toBe("anonymous");
  });

  it("bootstrap_exposes_distinct_error_for_network_and_5xx_but_not_for_401", async () => {
    const s = useSessionStore();
    setApi(fakeApi({ me: async () => null }));
    await s.bootstrap();
    expect(s.status).toBe("anonymous");
    expect(s.bootstrapError).toBeNull();
    for (const [err, code] of [
      [new ApiError(0, "network"), "network"],
      [new ApiError(503), "server"],
    ] as const) {
      setApi(
        fakeApi({
          me: async () => {
            throw err;
          },
        }),
      );
      await s.bootstrap();
      expect(s.status).toBe("anonymous");
      expect(s.bootstrapError).toBe(code);
    }
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    await s.bootstrap();
    expect(s.bootstrapError).toBeNull();
  });

  it("logout_is_ignored_while_pending", async () => {
    let release!: () => void;
    let calls = 0;
    const api = fakeApi({
      me: async () => ({ email: "user@example.test" }),
      logout: () => {
        calls++;
        return new Promise<void>((r) => (release = r));
      },
    });
    setApi(api);
    const s = useSessionStore();
    await s.bootstrap();
    const first = s.logout();
    expect(s.loggingOut).toBe(true);
    await s.logout();
    expect(calls).toBe(1);
    release();
    await first;
    expect(s.loggingOut).toBe(false);
  });

  it("login_failure_shows_generic_error_without_email_hint", async () => {
    const err = new ApiError(401, "invalid_credentials");
    setApi(
      fakeApi({
        login: async () => {
          throw err;
        },
      }),
    );
    const s = useSessionStore();
    await expect(s.login("a@example.test", "x")).rejects.toBe(err);
    expect(s.status).not.toBe("authenticated");
    expect(s.user).toBeNull();
    expect(err.message).not.toContain("a@example.test");
  });

  it("unauthorized_redirects_to_login_preserving_next", async () => {
    setApi(fakeApi({ me: async () => ({ email: "user@example.test" }) }));
    const s = useSessionStore();
    await s.bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/transactions?page=2");
    const push = vi.spyOn(router, "push");
    s.handleUnauthorized();
    s.handleUnauthorized(); // 401s simultâneos => uma navegação
    s.handleUnauthorized();
    await tick();
    expect(push).toHaveBeenCalledTimes(1);
    expect(s.status).toBe("anonymous");
    expect(router.currentRoute.value.name).toBe("login");
    expect(router.currentRoute.value.query.next).toBe("/transactions?page=2");
    // já no login: não faz loop nem sobrescreve next
    s.handleUnauthorized();
    await tick();
    expect(router.currentRoute.value.query.next).toBe("/transactions?page=2");
  });

  it("logout_clears_state_and_redirects", async () => {
    const api = fakeApi({ me: async () => ({ email: "user@example.test" }) });
    setApi(api);
    const s = useSessionStore();
    await s.bootstrap();
    const router = createRealRouter(createMemoryHistory());
    await router.push("/accounts");
    await s.logout();
    expect(api.auth.logout).toHaveBeenCalled();
    expect(s.user).toBeNull();
    expect(s.status).toBe("anonymous");
    expect(router.currentRoute.value.name).toBe("login");
  });

  it("logout_failure_keeps_user_signed_in_and_sets_error", async () => {
    setApi(
      fakeApi({
        me: async () => ({ email: "user@example.test" }),
        logout: async () => {
          throw new ApiError(500);
        },
      }),
    );
    const s = useSessionStore();
    await s.bootstrap();
    await s.logout();
    expect(s.status).toBe("authenticated");
    expect(s.user?.email).toBe("user@example.test");
    expect(s.error).toBe("logout_failed");
  });
});
