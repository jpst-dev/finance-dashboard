import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory } from "vue-router";
import { useSessionStore } from "./session";
import { setApi } from "../api/context";
import { createRealRouter } from "../router";
import { ApiError } from "../api/errors";
import { fakeApi } from "../testing";

const tick = () => new Promise((r) => setTimeout(r, 0));

beforeEach(() => {
  setActivePinia(createPinia());
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
    s.handleUnauthorized();
    s.handleUnauthorized(); // 401s simultâneos => uma navegação
    s.handleUnauthorized();
    await tick();
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
