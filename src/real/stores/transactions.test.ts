import { beforeEach, describe, expect, it, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { ApiError } from "../api/errors";
import { setApi } from "../api/context";
import type { ApiClient } from "../api";
import { fakeTxApi, tx } from "../testingTx";
import { useTransactionsStore } from "./transactions";

beforeEach(() => setActivePinia(createPinia()));

const input = { date: "2026-03-05", amount: "-10.00", description: "  Mercado  ", categoryId: null, state: "Normal" as const };

describe("transactions store", () => {
  it("update_always_sends_categoryId_null_to_clear", async () => {
    const api = fakeTxApi();
    const s = useTransactionsStore();
    await s.update("t1", input);
    const [id, body] = api.update.mock.calls[0] as unknown as [string, Record<string, unknown>];
    expect(id).toBe("t1");
    expect("categoryId" in body).toBe(true);
    expect(body.categoryId).toBeNull();
    expect(body.description).toBe("Mercado");
    expect(body.amount).toBe("-10.00");
    expect(body.state).toBe("Normal");
  });

  it("filters_reset_page_to_1", async () => {
    const api = fakeTxApi([tx()], 500);
    const s = useTransactionsStore();
    await s.setPage(3);
    expect(s.page).toBe(3);
    await s.setFilters({ month: "2026-03" });
    expect(s.page).toBe(1);
    const q = api.list.mock.calls.at(-1)![0] as Record<string, unknown>;
    expect(q).toMatchObject({ page: 1, month: "2026-03" });
    await s.setFilters({ month: undefined });
    expect("month" in (api.list.mock.calls.at(-1)![0] as object)).toBe(false);
  });

  it("page_beyond_range_shows_empty_state_not_error", async () => {
    const api = fakeTxApi([], 12);
    const s = useTransactionsStore();
    await s.setPage(9);
    expect(s.items).toEqual([]);
    expect(s.total).toBe(12);
    expect(s.error).toBeNull();
    expect(api.list).toHaveBeenCalledTimes(1); // sem laço de recarga
  });

  it("create_rejects_comma_thousand_formats_before_sending", async () => {
    const api = fakeTxApi();
    const s = useTransactionsStore();
    for (const bad of ["12,345", "1.234,56", "-0.00", "0", "1e3", ""]) {
      expect(await s.create({ ...input, accountId: "a1", amount: bad })).toBeNull();
      expect(s.error).toBe("invalid_amount");
    }
    expect(api.create).not.toHaveBeenCalled();
    expect(await s.create({ ...input, accountId: "a1", description: "   " })).toBeNull();
    expect(s.error).toBe("invalid_description");
    expect(await s.create({ ...input, accountId: "a1", description: "x".repeat(301) })).toBeNull();
    expect(api.create).not.toHaveBeenCalled();
    expect(await s.create({ ...input, accountId: "a1" })).not.toBeNull();
    expect(api.create).toHaveBeenCalledTimes(1);
  });

  it("mutations_reload_list_and_pending_count", async () => {
    const api = fakeTxApi();
    const s = useTransactionsStore();
    await s.create({ ...input, accountId: "a1" });
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(api.list.mock.calls.some((c) => (c[0] as { state?: string; size?: number }).state === "PendingClassification" && (c[0] as { size?: number }).size === 1)).toBe(true);
    await s.remove("t1");
    expect(api.list).toHaveBeenCalledTimes(4);
  });

  it("late_response_of_older_request_is_ignored", async () => {
    let releaseOld!: () => void;
    const old = new Promise<void>((r) => (releaseOld = r));
    const list = vi.fn(async (q: { month?: string }) => {
      if (!q.month) {
        await old;
        return { items: [tx({ id: "OLD" })], page: 1, size: 50, total: 1 };
      }
      return { items: [tx({ id: "NEW" })], page: 1, size: 50, total: 1 };
    });
    setApi({ transactions: { list } } as unknown as ApiClient);
    const s = useTransactionsStore();
    const first = s.load();
    await s.setFilters({ month: "2026-03" });
    expect(s.items[0].id).toBe("NEW");
    releaseOld();
    await first;
    expect(s.items[0].id).toBe("NEW");
    expect(s.loading).toBe(false);
  });

  it("errors_map_to_codes_never_raw_text", async () => {
    const api = fakeTxApi();
    api.list.mockRejectedValueOnce(new ApiError(500, "boom-9999.99", undefined, "raw 9999.99"));
    const s = useTransactionsStore();
    await s.load();
    expect(s.error).toBe("server");
  });

  it("classify_rolls_back_on_error", async () => {
    const api = fakeTxApi([tx({ id: "p1", state: "PendingClassification", categoryId: null })]);
    const s = useTransactionsStore();
    await s.load();
    api.update.mockRejectedValueOnce(new ApiError(0, "network"));
    const ok = await s.classify("p1", { categoryId: "c9" });
    expect(ok).toBe(false);
    expect(s.items[0].categoryId).toBeNull();
    expect(s.error).toBe("network");
  });
});
