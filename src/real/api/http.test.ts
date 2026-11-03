import { describe, it, expect, vi } from "vitest";
import { ApiError, createHttpApi } from "./index";

// Dados sintéticos apenas.
const ACCOUNT_ID = "00000000-0000-4000-8000-000000000001";
const TX_ID = "00000000-0000-4000-8000-0000000000aa";

type Reply = { status: number; body?: unknown; raw?: string };
type Call = { url: string; method: string; headers: Headers; body: BodyInit | null | undefined; credentials?: RequestCredentials };

/** fetch simulado: responde por "MÉTODO caminho" com uma fila de respostas; registra cada chamada. */
function fakeFetch(routes: Record<string, Reply | Reply[] | Error>) {
  const calls: Call[] = [];
  const queues = new Map<string, Reply[] | Error>();
  for (const [k, v] of Object.entries(routes)) queues.set(k, v instanceof Error ? v : Array.isArray(v) ? [...v] : [v]);
  let csrfCounter = 0;

  const fn = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = String(input);
    const method = (init.method ?? "GET").toUpperCase();
    calls.push({ url, method, headers: new Headers(init.headers), body: init.body, credentials: init.credentials });
    const path = url.split("?")[0];
    const key = `${method} ${path}`;
    if (key === "GET /api/auth/csrf" && !queues.has(key)) {
      csrfCounter += 1;
      return new Response(JSON.stringify({ token: `t${csrfCounter}` }), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    const q = queues.get(key);
    if (q instanceof Error) throw q;
    if (!q || q.length === 0) throw new Error(`rota não simulada: ${key}`);
    const r = q.length > 1 ? q.shift()! : q[0];
    const text = r.raw ?? (r.body === undefined ? null : JSON.stringify(r.body));
    return new Response(r.status === 204 ? null : text, {
      status: r.status,
      headers: r.raw !== undefined ? { "Content-Type": "text/html" } : { "Content-Type": "application/json" },
    });
  });
  return { fetch: fn as unknown as typeof fetch, calls };
}

const csrfCalls = (calls: Call[]) => calls.filter((c) => c.url === "/api/auth/csrf");
const tx = { id: TX_ID, accountId: ACCOUNT_ID, date: "2026-01-02", purchaseDate: null, statementDate: null, amount: "-10.00",
  description: "Teste", originalDescription: "Teste", categoryId: null, state: "Normal", origin: "Manual", editedByUser: false, counterpartyName: null };

describe("createHttpApi", () => {
  it("Mutation_fetches_csrf_once_and_sends_header", async () => {
    const { fetch, calls } = fakeFetch({
      [`DELETE /api/transactions/${TX_ID}`]: { status: 204 },
      [`PUT /api/transactions/${TX_ID}`]: { status: 200, body: tx },
    });
    const api = createHttpApi({ fetch });

    await Promise.all([
      api.transactions.remove(TX_ID),
      api.transactions.update(TX_ID, { date: "2026-01-02", amount: "-10.00", description: "Teste", categoryId: null, state: null }),
    ]);
    await api.transactions.remove(TX_ID);

    expect(csrfCalls(calls)).toHaveLength(1);
    const mutations = calls.filter((c) => c.method !== "GET");
    expect(mutations).toHaveLength(3);
    for (const m of mutations) {
      expect(m.headers.get("X-XSRF-TOKEN")).toBe("t1");
      expect(m.credentials).toBe("same-origin");
    }
    const put = mutations.find((m) => m.method === "PUT")!;
    expect(put.headers.get("Content-Type")).toBe("application/json");
    expect(JSON.parse(String(put.body))).toMatchObject({ amount: "-10.00" });
  });

  it("Csrf_token_is_dropped_after_login_and_logout", async () => {
    const { fetch, calls } = fakeFetch({
      "POST /api/auth/login": { status: 200 },
      "POST /api/auth/logout": { status: 204 },
      [`DELETE /api/transactions/${TX_ID}`]: { status: 204 },
    });
    const api = createHttpApi({ fetch });

    await api.auth.login("pessoa@example.test", "senha-sintetica-123");
    await api.transactions.remove(TX_ID);
    await api.auth.logout();
    await api.auth.login("pessoa@example.test", "senha-sintetica-123");

    const seq = calls.map((c) => `${c.method} ${c.url} ${c.headers.get("X-XSRF-TOKEN") ?? ""}`.trim());
    expect(seq).toEqual([
      "GET /api/auth/csrf",
      "POST /api/auth/login t1",
      "GET /api/auth/csrf",
      `DELETE /api/transactions/${TX_ID} t2`,
      "POST /api/auth/logout t2",
      "GET /api/auth/csrf",
      "POST /api/auth/login t3",
    ]);
    const login = calls.find((c) => c.url === "/api/auth/login")!;
    expect(JSON.parse(String(login.body))).toEqual({ email: "pessoa@example.test", password: "senha-sintetica-123" });
  });

  it("Antiforgery_400_refetches_token_and_retries_once", async () => {
    const { fetch, calls } = fakeFetch({
      [`DELETE /api/transactions/${TX_ID}`]: [{ status: 204 }, { status: 400 }, { status: 204 }, { status: 400 }, { status: 400 }],
    });
    const api = createHttpApi({ fetch });

    await api.transactions.remove(TX_ID); // token t1 em memória
    await api.transactions.remove(TX_ID); // 400 com t1 → busca t2 → repete com t2 → 204
    const deletes = calls.filter((c) => c.method === "DELETE").map((c) => c.headers.get("X-XSRF-TOKEN"));
    expect(deletes).toEqual(["t1", "t1", "t2"]);
    expect(csrfCalls(calls)).toHaveLength(2);

    // Segunda falha seguida: uma única nova tentativa, depois o erro chega ao chamador.
    const err = await api.transactions.remove(TX_ID).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(400);
    expect(calls.filter((c) => c.method === "DELETE")).toHaveLength(5);
    expect(csrfCalls(calls)).toHaveLength(3);
  });

  it("Unauthorized_calls_handler_but_not_for_login_or_me", async () => {
    const onUnauthorized = vi.fn();
    const { fetch } = fakeFetch({
      "POST /api/auth/login": { status: 401, body: { error: "invalid_credentials" } },
      "GET /api/auth/me": { status: 401 },
      "GET /api/accounts": { status: 401 },
    });
    const api = createHttpApi({ fetch, onUnauthorized });

    const loginErr = await api.auth.login("pessoa@example.test", "errada-sintetica").catch((e) => e);
    expect(loginErr).toBeInstanceOf(ApiError);
    expect(loginErr.code).toBe("invalid_credentials");
    expect(await api.auth.me()).toBeNull();
    expect(onUnauthorized).not.toHaveBeenCalled();

    const err = await api.accounts.list().catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(401);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });

  it("Unauthorized_drops_csrf_token_before_next_mutation", async () => {
    const { fetch, calls } = fakeFetch({
      [`DELETE /api/transactions/${TX_ID}`]: { status: 204 },
      "GET /api/accounts": { status: 401 },
    });
    const api = createHttpApi({ fetch, onUnauthorized: () => {} });

    await api.transactions.remove(TX_ID); // t1
    await api.accounts.list().catch(() => {}); // sessão expirou: identidade mudou
    await api.transactions.remove(TX_ID); // precisa de token novo
    expect(calls.filter((c) => c.method === "DELETE").map((c) => c.headers.get("X-XSRF-TOKEN"))).toEqual(["t1", "t2"]);
  });

  it("Error_body_code_maps_to_ApiError", async () => {
    const { fetch } = fakeFetch({
      "POST /api/imports/b1/confirm": { status: 422, body: { error: "invalid_override" } },
      "GET /api/imports/b2": { status: 502, raw: "<html>Bad Gateway</html>" },
      "GET /api/imports/b3": { status: 404 },
    });
    const api = createHttpApi({ fetch });

    const e1 = await api.imports.confirm("b1", { acknowledgeBlockingWarnings: false, overrides: [] }).catch((e) => e);
    expect(e1).toBeInstanceOf(ApiError);
    expect(e1.status).toBe(422);
    expect(e1.code).toBe("invalid_override");

    const e2 = await api.imports.get("b2").catch((e) => e);
    expect(e2).toBeInstanceOf(ApiError);
    expect(e2.status).toBe(502);
    expect(e2.code).toBeUndefined();

    const e3 = await api.imports.get("b3").catch((e) => e);
    expect(e3).toBeInstanceOf(ApiError);
    expect(e3.status).toBe(404);
  });

  it("Concurrent_antiforgery_400s_share_one_csrf_refetch", async () => {
    const { fetch, calls } = fakeFetch({
      [`DELETE /api/transactions/${TX_ID}`]: [{ status: 400 }, { status: 400 }, { status: 204 }],
    });
    const api = createHttpApi({ fetch });
    await Promise.all([api.transactions.remove(TX_ID), api.transactions.remove(TX_ID)]);
    expect(csrfCalls(calls)).toHaveLength(2); // o inicial + UM refetch compartilhado
    const deletes = calls.filter((c) => c.method === "DELETE");
    expect(deletes).toHaveLength(4);
    expect(deletes.slice(2).map((c) => c.headers.get("X-XSRF-TOKEN"))).toEqual(["t2", "t2"]);
  });

  it("Bad_request_with_error_body_is_not_retried", async () => {
    const { fetch, calls } = fakeFetch({ [`DELETE /api/transactions/${TX_ID}`]: { status: 400, body: { error: "invalid" } } });
    const api = createHttpApi({ fetch });
    const e = await api.transactions.remove(TX_ID).catch((x) => x);
    expect(e).toBeInstanceOf(ApiError);
    expect(e.code).toBe("invalid");
    expect(calls.filter((c) => c.method === "DELETE")).toHaveLength(1);
    expect(csrfCalls(calls)).toHaveLength(1);
  });

  it("Me_without_usable_email_is_an_invalid_response", async () => {
    const { fetch } = fakeFetch({ "GET /api/auth/me": [{ status: 200, body: { email: null } }, { status: 200, body: {} }] });
    const api = createHttpApi({ fetch });
    for (let i = 0; i < 2; i++) {
      const e = await api.auth.me().catch((x) => x);
      expect(e).toBeInstanceOf(ApiError);
      expect(e.code).toBe("invalid_response");
    }
  });

  it("Me_returns_null_on_401", async () => {
    const { fetch } = fakeFetch({
      "GET /api/auth/me": [{ status: 401 }, { status: 200, body: { email: "pessoa@example.test" } }],
    });
    const api = createHttpApi({ fetch });
    expect(await api.auth.me()).toBeNull();
    expect(await api.auth.me()).toEqual({ email: "pessoa@example.test" });
  });

  it("Preview_sends_multipart_without_manual_content_type", async () => {
    const { fetch, calls } = fakeFetch({ "POST /api/imports": { status: 200, body: { batchId: "b1" } } });
    const api = createHttpApi({ fetch });
    const file = new File(["<OFX></OFX>"], "extrato-sintetico.ofx", { type: "application/x-ofx" });

    await api.imports.preview(ACCOUNT_ID, file);

    const post = calls.find((c) => c.method === "POST")!;
    expect(post.headers.has("Content-Type")).toBe(false);
    expect(post.headers.get("X-XSRF-TOKEN")).toBe("t1");
    expect(post.body).toBeInstanceOf(FormData);
    const form = post.body as FormData;
    expect(form.get("accountId")).toBe(ACCOUNT_ID);
    expect((form.get("file") as File).name).toBe("extrato-sintetico.ofx");
  });

  it("Network_failure_becomes_ApiError_with_status_0", async () => {
    const { fetch, calls } = fakeFetch({
      "GET /api/accounts": new TypeError("Failed to fetch"),
      [`DELETE /api/transactions/${TX_ID}`]: new TypeError("Failed to fetch"),
    });
    const api = createHttpApi({ fetch });

    const e1 = await api.accounts.list().catch((e) => e);
    expect(e1).toBeInstanceOf(ApiError);
    expect(e1.status).toBe(0);

    const e2 = await api.transactions.remove(TX_ID).catch((e) => e);
    expect(e2).toBeInstanceOf(ApiError);
    expect(e2.status).toBe(0);
    // Mutação nunca é repetida em falha de rede.
    expect(calls.filter((c) => c.method === "DELETE")).toHaveLength(1);
  });

  it("Query_string_omits_undefined_and_ids_are_encoded", async () => {
    const { fetch, calls } = fakeFetch({
      "GET /api/transactions": { status: 200, body: { items: [], page: 1, size: 50, total: 0 } },
      "GET /api/summary": { status: 200, body: {} },
      "GET /api/imports/a%2Fb": { status: 200, body: {} },
    });
    const api = createHttpApi({ fetch });

    await api.transactions.list({ state: "PendingClassification", page: 2, accountId: undefined });
    await api.summary.month("2026-01");
    await api.imports.get("a/b");

    expect(calls.map((c) => c.url)).toEqual([
      "/api/transactions?state=PendingClassification&page=2",
      "/api/summary?month=2026-01",
      "/api/imports/a%2Fb",
    ]);
  });
});
