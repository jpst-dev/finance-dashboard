import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, DEMO_READ_ONLY_CODE } from "../real/api/errors";
import type { ApiClient } from "../real/api/client";
import { DEMO_FIXTURES as f } from "./fixtures";
import { createStaticApi } from "./staticApi";

const api = (): ApiClient => createStaticApi(f);
const ACC = f.accounts[0].id;

async function status(p: Promise<unknown>): Promise<number | undefined> {
  try {
    await p;
  } catch (e) {
    expect(e).toBeInstanceOf(ApiError);
    return (e as ApiError).status;
  }
  return undefined;
}

afterEach(() => vi.unstubAllGlobals());

describe("createStaticApi", () => {
  it("me_returns_fixed_visitor", async () => {
    expect(await api().auth.me()).toEqual(f.meta.user);
  });

  it("lists_return_copies", async () => {
    const a = api();
    const first = await a.accounts.list();
    first[0].name = "mutado";
    first.pop();
    expect(await a.accounts.list()).toEqual(f.accounts);
    const cats = await a.categories.list();
    cats.length = 0;
    expect(await a.categories.list()).toEqual(f.categories);
    const page = await a.transactions.list({ size: 5 });
    page.items[0].description = "mutado";
    expect((await a.transactions.list({ size: 5 })).items[0].description).toBe(f.transactions[0].description);

    const me = await a.auth.me();
    me!.email = "mutado@exemplo.invalid";
    expect(await a.auth.me()).toEqual(f.meta.user);

    const cpId = Object.keys(f.checkpoints).find((k) => f.checkpoints[k].length > 0)!;
    const cps = await a.accounts.checkpoints(cpId);
    cps[0].amount = "0.00";
    cps.pop();
    expect(await a.accounts.checkpoints(cpId)).toEqual(f.checkpoints[cpId]);

    const sum = await a.summary.month("2026-10");
    sum.income = "0.00";
    sum.byCategory.length = 0;
    sum.pending.count = 999;
    expect(await a.summary.month("2026-10")).toEqual(f.summaries["2026-10"]);

    const list = await a.imports.list();
    list[0].state = "Undone";
    list.pop();
    expect(await a.imports.list()).toEqual(f.imports);

    const id = f.meta.previewBatchId;
    const got = await a.imports.get(id);
    got.lines[0].description = "mutado";
    got.lines.pop();
    expect(await a.imports.get(id)).toEqual(f.importDetails[id]);

    const prev = await a.imports.preview(ACC, new File(["x"], "x.ofx"));
    prev.lines[0].description = "mutado";
    prev.warnings.length = 0;
    expect(await a.imports.preview(ACC, new File(["x"], "x.ofx"))).toEqual(f.importDetails[id]);
  });

  it.each(["constructor", "__proto__", "toString", "hasOwnProperty"])("inherited_key_%s_is_not_a_record", async (key) => {
    const a = api();
    expect(await status(a.accounts.checkpoints(key))).toBe(404);
    expect(await status(a.imports.get(key))).toBe(404);
    expect(await status(a.summary.month(key))).toBe(400);
  });

  it("transactions_filter_by_account_month_category_state_preserving_order", async () => {
    const t0 = f.transactions.find((t) => t.categoryId && t.date.startsWith("2026-09"))!;
    const q = { accountId: t0.accountId, month: "2026-09", categoryId: t0.categoryId!, state: t0.state, size: 200 };
    const expected = f.transactions.filter(
      (t) => t.accountId === q.accountId && t.date.startsWith(q.month) && t.categoryId === q.categoryId && t.state === q.state,
    );
    expect(expected.length).toBeGreaterThan(0);
    const r = await api().transactions.list(q);
    expect(r.items).toEqual(expected);
    expect(r.total).toBe(expected.length);
    expect(r.page).toBe(1);
  });

  it("transactions_paginate_and_out_of_range_page_is_empty", async () => {
    expect(f.transactions.length).toBeGreaterThan(20);
    const r = await api().transactions.list({ size: 10, page: 2 });
    expect(r.items).toEqual(f.transactions.slice(10, 20));
    expect(r.total).toBe(f.transactions.length);
    const far = await api().transactions.list({ size: 10, page: 9999 });
    expect(far.items).toEqual([]);
    expect(far.total).toBe(f.transactions.length);
  });

  it("transactions_size_capped_at_200", async () => {
    const r = await api().transactions.list({ size: 5000 });
    expect(r.size).toBe(200);
    expect(r.items.length).toBe(Math.min(200, f.transactions.length));
  });

  it("transactions_invalid_page_size_or_month_is_400", async () => {
    const a = api();
    expect(await status(a.transactions.list({ page: 0 }))).toBe(400);
    expect(await status(a.transactions.list({ size: 0 }))).toBe(400);
    expect(await status(a.transactions.list({ month: "2026-13" }))).toBe(400);
    expect(await status(a.transactions.list({ month: "26-01" }))).toBe(400);
  });

  it("summary_returns_generated_month", async () => {
    expect(await api().summary.month("2026-10")).toEqual(f.summaries["2026-10"]);
  });

  it("summary_outside_fixtures_returns_generated_empty_with_requested_month", async () => {
    expect(await api().summary.month("2019-01")).toEqual({ ...f.emptySummary, month: "2019-01" });
  });

  it("summary_with_account_fails_loudly", async () => {
    await expect(api().summary.month("2026-10", ACC)).rejects.toThrow();
    await expect(api().summary.month("2026-10", ACC)).rejects.not.toBeInstanceOf(ApiError);
  });

  it("summary_invalid_month_is_400", async () => {
    expect(await status(api().summary.month("2026-00"))).toBe(400);
  });

  it("checkpoints_known_account_and_404_for_unknown", async () => {
    const a = api();
    expect(await a.accounts.checkpoints(ACC)).toEqual(f.checkpoints[ACC]);
    expect(await status(a.accounts.checkpoints("nao-existe"))).toBe(404);
  });

  it("import_preview_ignores_account_and_file", async () => {
    const boom = vi.fn(() => {
      throw new Error("File tocado");
    });
    const file = new File(["x"], "x.csv");
    Object.assign(file, { arrayBuffer: boom, text: boom, stream: boom, slice: boom });
    const reader = vi.fn();
    vi.stubGlobal("FileReader", reader);
    const r = await api().imports.preview("qualquer", file);
    expect(r).toEqual(f.importDetails[f.meta.previewBatchId]);
    expect(boom).not.toHaveBeenCalled();
    expect(reader).not.toHaveBeenCalled();
  });

  it("import_list_get_and_404", async () => {
    const a = api();
    expect(await a.imports.list()).toEqual(f.imports);
    const id = f.imports[0].batchId;
    expect(await a.imports.get(id)).toEqual(f.importDetails[id]);
    expect(await status(a.imports.get("nao-existe"))).toBe(404);
  });

  const mutations: [string, (a: ApiClient) => Promise<unknown>][] = [
    ["auth.login", (a) => a.auth.login("a@b.c", "x")],
    ["auth.logout", (a) => a.auth.logout()],
    ["accounts.create", (a) => a.accounts.create({} as never)],
    ["accounts.update", (a) => a.accounts.update(ACC, {} as never)],
    ["accounts.addCheckpoint", (a) => a.accounts.addCheckpoint(ACC, {} as never)],
    ["transactions.create", (a) => a.transactions.create({} as never)],
    ["transactions.update", (a) => a.transactions.update("id", {} as never)],
    ["transactions.remove", (a) => a.transactions.remove("id")],
    ["imports.confirm", (a) => a.imports.confirm("id", {} as never)],
    ["imports.undo", (a) => a.imports.undo("id")],
  ];
  it.each(mutations)("every_mutation_rejects_with_demo_read_only %s", async (_n, call) => {
    const e = await call(api()).then(
      () => null,
      (x: unknown) => x,
    );
    expect(e).toBeInstanceOf(ApiError);
    expect((e as ApiError).status).toBe(403);
    expect((e as ApiError).code).toBe(DEMO_READ_ONLY_CODE);
  });

  it("works_without_structured_clone_and_still_returns_copies", async () => {
    vi.stubGlobal("structuredClone", undefined);
    const a = api();
    expect(await a.auth.me()).toEqual(f.meta.user);
    const accs = await a.accounts.list();
    expect(accs).toEqual(f.accounts);
    accs[0].name = "mutado";
    expect((await a.accounts.list())[0].name).toBe(f.accounts[0].name);
    const s = await a.summary.month("2026-10");
    expect(s).toEqual(f.summaries["2026-10"]);
    s.income = "0.00";
    expect((await a.summary.month("2026-10")).income).toBe(f.summaries["2026-10"].income);
  });

  it("no_network_used", async () => {
    const fetchSpy = vi.fn();
    const xhr = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);
    vi.stubGlobal("XMLHttpRequest", xhr);
    const a = api();
    await a.auth.me();
    await a.accounts.list();
    await a.accounts.checkpoints(ACC);
    await a.categories.list();
    await a.transactions.list({});
    await a.summary.month("2026-10");
    await a.imports.list();
    await a.imports.get(f.meta.previewBatchId);
    await a.imports.preview(ACC, new File(["x"], "x.csv"));
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(xhr).not.toHaveBeenCalled();
  });
});
