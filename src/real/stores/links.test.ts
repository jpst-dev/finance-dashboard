import { beforeEach, describe, expect, it } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import { fakeLinksApi, link } from "../testingLinks";
import { useLinksStore } from "./links";
import { useTransactionsStore } from "./transactions";

beforeEach(() => setActivePinia(createPinia()));

describe("links store", () => {
  it("load_splits_suggested_and_confirmed", async () => {
    const api = fakeLinksApi([link({ id: "s1" }), link({ id: "c1", state: "Confirmed" })]);
    const s = useLinksStore();
    await s.load();
    expect(s.suggested.map((l) => l.id)).toEqual(["s1"]);
    expect(s.confirmed.map((l) => l.id)).toEqual(["c1"]);
    const states = api.links.list.mock.calls.map((c) => (c[0] as { state: string }).state).sort();
    expect(states).toEqual(["Confirmed", "Suggested"]);
    expect(s.error).toBeNull();
  });

  it("confirm_reloads_links_and_pending_list_and_count", async () => {
    const api = fakeLinksApi([link({ id: "s1" })]);
    const s = useLinksStore();
    const t = useTransactionsStore();
    await s.load();
    api.transactions.list.mockClear();
    expect(await s.confirm("s1")).toBe(true);
    expect(api.links.confirm).toHaveBeenCalledWith("s1");
    expect(s.suggested).toEqual([]);
    expect(s.confirmed.map((l) => l.id)).toEqual(["s1"]);
    // lista de pendências (com filtros) e contador (size 1)
    const sizes = api.transactions.list.mock.calls.map((c) => (c[0] as { size?: number }).size);
    expect(sizes).toContain(1);
    expect(sizes.length).toBeGreaterThanOrEqual(2);
    expect(t.pendingCount).toBe(1);
  });

  it("reject_and_undo_reload", async () => {
    const api = fakeLinksApi([link({ id: "s1" }), link({ id: "c1", state: "Confirmed" })]);
    const s = useLinksStore();
    await s.load();
    expect(await s.reject("s1")).toBe(true);
    expect(s.suggested).toEqual([]);
    expect(await s.undo("c1")).toBe(true);
    expect(api.links.undo).toHaveBeenCalledWith("c1");
    expect(s.confirmed).toEqual([]);
  });

  it("maps_409_to_link_conflict_and_reloads", async () => {
    const api = fakeLinksApi([link({ id: "s1" })]);
    api.links.confirm.mockRejectedValueOnce(new ApiError(409));
    const s = useLinksStore();
    await s.load();
    api.links.list.mockClear();
    expect(await s.confirm("s1")).toBe(false);
    expect(s.error).toBe("link_conflict");
    expect(api.links.list).toHaveBeenCalled();
  });

  it("maps_other_errors_via_errorCode", async () => {
    const api = fakeLinksApi();
    api.links.list.mockRejectedValueOnce(new ApiError(0));
    const s = useLinksStore();
    await s.load();
    expect(s.error).toBe("network");
  });
});
