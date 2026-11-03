import { beforeEach, describe, expect, it } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import { fakeLinksApi, link, oneSided } from "../testingLinks";
import { useLinksStore } from "./links";
import { useTransactionsStore } from "./transactions";

beforeEach(() => setActivePinia(createPinia()));

describe("links store S3(b)", () => {
  it("one_sided_links_are_listed_without_filtering", async () => {
    fakeLinksApi([oneSided({ id: "o1" }), oneSided({ id: "o2", state: "Confirmed" }), link({ id: "s1" })]);
    const s = useLinksStore();
    await s.load();
    expect(s.suggested.map((l) => l.id)).toEqual(["o1", "s1"]);
    expect(s.confirmed.map((l) => l.id)).toEqual(["o2"]);
  });

  it("reconcile_calls_api_keeps_summary_and_reloads", async () => {
    const api = fakeLinksApi([link({ id: "s1" })]);
    api.links.reconcile.mockResolvedValueOnce({ linked: 2, suggested: 1, reclassified: 0 });
    const s = useLinksStore();
    expect(await s.reconcile()).toEqual({ linked: 2, suggested: 1, reclassified: 0 });
    expect(s.lastReconcile).toEqual({ linked: 2, suggested: 1, reclassified: 0 });
    expect(api.links.list).toHaveBeenCalled();
    expect(api.transactions.list.mock.calls.map((c) => (c[0] as { size?: number }).size)).toContain(1);
    expect(s.busy).toBe(false);
    expect(s.suggested.map((l) => l.id)).toEqual(["s1"]);
  });

  it("reconcile_failure_returns_null_and_sets_error", async () => {
    const api = fakeLinksApi();
    api.links.reconcile.mockRejectedValueOnce(new ApiError(0));
    const s = useLinksStore();
    expect(await s.reconcile()).toBeNull();
    expect(s.error).toBe("network");
    expect(s.lastReconcile).toBeNull();
  });
});

describe("transactions store markOwn / markNoCounterpart", () => {
  it("markOwn_calls_api_and_reloads_links_and_pending", async () => {
    const api = fakeLinksApi();
    const t = useTransactionsStore();
    expect(await t.markOwn("t1")).toBe(true);
    expect(api.transactions.markOwn).toHaveBeenCalledWith("t1");
    expect(api.links.list).toHaveBeenCalled();
    expect(api.transactions.list.mock.calls.map((c) => (c[0] as { size?: number }).size)).toContain(1);
    expect(useLinksStore().busy).toBe(false);
  });

  it("markNoCounterpart_calls_api_and_reloads", async () => {
    const api = fakeLinksApi();
    const t = useTransactionsStore();
    expect(await t.markNoCounterpart("t1")).toBe(true);
    expect(api.transactions.markNoCounterpart).toHaveBeenCalledWith("t1");
    expect(api.links.list).toHaveBeenCalled();
  });

  it.each([
    [404, undefined, "not_found"],
    [409, undefined, "link_conflict"],
    [422, "not_applicable", "not_applicable"],
  ])("maps_%i_to_%s_and_keeps_error_after_reload", async (status, code, expected) => {
    const api = fakeLinksApi();
    api.transactions.markOwn.mockRejectedValueOnce(new ApiError(status, code));
    const t = useTransactionsStore();
    expect(await t.markOwn("t1")).toBe(false);
    expect(t.error).toBe(expected);
    expect(api.links.list).toHaveBeenCalled();
  });
});
