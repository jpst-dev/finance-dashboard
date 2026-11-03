import { afterEach, describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import { defineComponent, h } from "vue";
import { APP_CONFIG_KEY, getAppConfig, setAppConfig, useAppConfig } from "./appConfig";

afterEach(() => setAppConfig(null));

describe("appConfig", () => {
  it("defaults_match_current_real_behavior", () => {
    expect(getAppConfig()).toEqual({ readOnly: false, hideLogout: false, fixedToday: null });
  });
  it("partial_config_merges_with_defaults", () => {
    setAppConfig({ readOnly: true });
    expect(getAppConfig().readOnly).toBe(true);
    expect(getAppConfig().hideLogout).toBe(false);
    expect(getAppConfig().fixedToday).toBeNull();
  });
  it("invalid_fixed_today_throws", () => {
    expect(() => setAppConfig({ fixedToday: "2026-10" })).toThrow();
    expect(() => setAppConfig({ fixedToday: "15/10/2026" })).toThrow();
  });
  it("null_restores_defaults", () => {
    setAppConfig({ readOnly: true, hideLogout: true, fixedToday: "2026-10-15" });
    setAppConfig(null);
    expect(getAppConfig()).toEqual({ readOnly: false, hideLogout: false, fixedToday: null });
  });
  it("useAppConfig_prefers_injected_value", () => {
    setAppConfig({ readOnly: false });
    let seen: unknown = null;
    const Child = defineComponent({
      setup() {
        seen = useAppConfig();
        return () => h("div");
      },
    });
    const injected = { readOnly: true, hideLogout: true, fixedToday: "2026-10-15" };
    mount(Child, { global: { provide: { [APP_CONFIG_KEY as symbol]: injected } } });
    expect(seen).toBe(injected);
  });
});
