// Implementação HTTP da ApiClient (mesma origem; cookie de sessão + token antiforgery).
// Contrato verificado em backend/src/FinanceDashboard.Api/** e backend/openapi/v1.json.
import type { ApiClient } from "./client";
import { ApiError, NETWORK_ERROR_CODE } from "./errors";
import type {
  Account,
  Category,
  Checkpoint,
  ConfirmResult,
  CsrfTokenResponse,
  CurrentUser,
  ImportBatch,
  ImportPreview,
  LinkPage,
  LinkResponse,
  LoginBody,
  Page,
  ReconcileResult,
  Summary,
  Transaction,
} from "./types";

export type HttpApiOptions = {
  /** fetch injetável (testes); padrão: o global. */
  fetch?: typeof fetch;
  /** Chamado em qualquer 401, exceto de auth.login (credencial errada) e auth.me (sondagem de sessão). */
  onUnauthorized?: () => void;
};

/** Cabeçalho exigido pelo backend em toda mutação (AuthEndpoints.AntiforgeryHeaderName). */
const CSRF_HEADER = "X-XSRF-TOKEN";

type Query = Record<string, string | number | undefined>;
type RequestSpec = {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  json?: unknown;
  form?: FormData;
  query?: Query;
  /** false em login/me: um 401 ali não é "sessão expirada". */
  notifyUnauthorized?: boolean;
};

const enc = encodeURIComponent;

function withQuery(path: string, query?: Query): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) if (v !== undefined) params.append(k, String(v));
  const qs = params.toString();
  return qs ? `${path}?${qs}` : path;
}

/** Lê o corpo sem nunca lançar: texto vazio ou não-JSON → undefined. */
async function readBody(res: Response): Promise<{ text: string; json: unknown }> {
  let text = "";
  try {
    text = await res.text();
  } catch {
    return { text: "", json: undefined };
  }
  if (!text) return { text, json: undefined };
  try {
    return { text, json: JSON.parse(text) };
  } catch {
    return { text, json: undefined };
  }
}

function errorFields(json: unknown): { code?: string; field?: string } {
  if (!json || typeof json !== "object") return {};
  const o = json as Record<string, unknown>;
  return {
    code: typeof o.error === "string" ? o.error : undefined,
    field: typeof o.field === "string" ? o.field : undefined,
  };
}

export function createHttpApi(opts: HttpApiOptions = {}): ApiClient {
  const doFetch: typeof fetch = opts.fetch ?? ((input, init) => globalThis.fetch(input, init));

  // Token antiforgery só em memória. É amarrado à identidade (anônimo ≠ logado), então é descartado após
  // login, logout, 401 e 400 de antiforgery. `generation` invalida uma busca em andamento de uma identidade antiga.
  let token: string | null = null;
  let pending: Promise<string> | null = null;
  let generation = 0;

  function dropToken() {
    token = null;
    pending = null;
    generation += 1;
  }

  async function fetchCsrf(): Promise<string> {
    let res: Response;
    try {
      res = await doFetch("/api/auth/csrf", { method: "GET", credentials: "same-origin", headers: { Accept: "application/json" } });
    } catch {
      throw new ApiError(0, NETWORK_ERROR_CODE);
    }
    const { json } = await readBody(res);
    const t = (json as Partial<CsrfTokenResponse> | undefined)?.token;
    if (!res.ok || typeof t !== "string" || !t) throw new ApiError(res.ok ? 500 : res.status, errorFields(json).code);
    return t;
  }

  function getToken(): Promise<string> {
    if (token) return Promise.resolve(token);
    if (!pending) {
      const gen = generation;
      const p = fetchCsrf().then(
        (t) => {
          if (gen === generation) {
            token = t;
            pending = null;
          }
          return t;
        },
        (e) => {
          if (gen === generation) pending = null;
          throw e;
        },
      );
      pending = p;
    }
    return pending;
  }

  async function send(path: string, spec: RequestSpec, csrf: string | null): Promise<Response> {
    const headers: Record<string, string> = { Accept: "application/json" };
    let body: BodyInit | undefined;
    if (spec.form) {
      body = spec.form; // sem Content-Type manual: o navegador define multipart com o boundary
    } else if (spec.json !== undefined) {
      headers["Content-Type"] = "application/json";
      body = JSON.stringify(spec.json);
    }
    if (csrf) headers[CSRF_HEADER] = csrf;
    try {
      return await doFetch(withQuery(path, spec.query), { method: spec.method ?? "GET", credentials: "same-origin", headers, body });
    } catch {
      // Falha de rede: nunca repetir (uma mutação pode ter chegado ao servidor).
      throw new ApiError(0, NETWORK_ERROR_CODE);
    }
  }

  async function request<T>(path: string, spec: RequestSpec = {}): Promise<T> {
    const mutation = (spec.method ?? "GET") !== "GET";
    let used = mutation ? await getToken() : null;
    let res = await send(path, spec, used);
    let parsed = await readBody(res);

    // Antiforgery inválido responde 400 SEM corpo (AntiforgeryFilter). Os 400 de validação também vêm sem corpo
    // e nada gravam, então repetir uma vez com token novo é seguro; 400 com {error} não é antiforgery.
    if (mutation && res.status === 400 && errorFields(parsed.json).code === undefined) {
      if (token === used) dropToken();
      used = await getToken();
      res = await send(path, spec, used);
      parsed = await readBody(res);
    }

    if (res.ok) {
      if (res.status === 204 || !parsed.text) return undefined as T;
      if (parsed.json === undefined) throw new ApiError(res.status, "invalid_response");
      return parsed.json as T;
    }

    if (res.status === 401) {
      dropToken();
      if (spec.notifyUnauthorized !== false) opts.onUnauthorized?.();
    }
    const { code, field } = errorFields(parsed.json);
    throw new ApiError(res.status, code, field);
  }

  return {
    auth: {
      async me() {
        try {
          const me = await request<Partial<CurrentUser> | undefined>("/api/auth/me", { notifyUnauthorized: false });
          // Sessão sem e-mail utilizável não é uma sessão válida: erro de resposta, não "anônimo".
          if (typeof me?.email !== "string" || !me.email) throw new ApiError(500, "invalid_response");
          return { email: me.email };
        } catch (e) {
          if (e instanceof ApiError && e.status === 401) return null;
          throw e;
        }
      },
      async login(email, password) {
        // Um 400 sem corpo é repetido uma vez pelo request() (suposto antiforgery): na prática gasta 2 tentativas do rate limit.
        const body: LoginBody = { email, password };
        try {
          await request<void>("/api/auth/login", { method: "POST", json: body, notifyUnauthorized: false });
        } finally {
          dropToken(); // a identidade mudou: o próximo token precisa ser emitido para ela
        }
      },
      async logout() {
        try {
          await request<void>("/api/auth/logout", { method: "POST" });
        } finally {
          dropToken();
        }
      },
    },
    accounts: {
      list: () => request<Account[]>("/api/accounts"),
      create: (b) => request<Account>("/api/accounts", { method: "POST", json: b }),
      update: (id, b) => request<Account>(`/api/accounts/${enc(id)}`, { method: "PUT", json: b }),
      checkpoints: (id) => request<Checkpoint[]>(`/api/accounts/${enc(id)}/checkpoints`),
      addCheckpoint: (id, b) => request<Checkpoint>(`/api/accounts/${enc(id)}/checkpoints`, { method: "POST", json: b }),
    },
    categories: {
      list: () => request<Category[]>("/api/categories"),
    },
    transactions: {
      list: (q) => request<Page<Transaction>>("/api/transactions", { query: { ...q } }),
      create: (b) => request<Transaction>("/api/transactions", { method: "POST", json: b }),
      update: (id, b) => request<Transaction>(`/api/transactions/${enc(id)}`, { method: "PUT", json: b }),
      remove: (id) => request<void>(`/api/transactions/${enc(id)}`, { method: "DELETE" }),
      markOwn: (id) => request<LinkResponse>(`/api/transactions/${enc(id)}/own-transfer`, { method: "POST" }),
      markNoCounterpart: (id) => request<LinkResponse>(`/api/transactions/${enc(id)}/no-counterpart`, { method: "POST" }),
    },
    summary: {
      month: (month, accountId) => request<Summary>("/api/summary", { query: { month, accountId } }),
    },
    imports: {
      preview(accountId, file) {
        const form = new FormData();
        form.append("file", file, file.name);
        form.append("accountId", accountId);
        return request<ImportPreview>("/api/imports", { method: "POST", form });
      },
      list: () => request<ImportBatch[]>("/api/imports"),
      get: (id) => request<ImportPreview>(`/api/imports/${enc(id)}`),
      confirm: (id, b) => request<ConfirmResult>(`/api/imports/${enc(id)}/confirm`, { method: "POST", json: b }),
      undo: (id) => request<void>(`/api/imports/${enc(id)}/undo`, { method: "POST" }),
    },
    links: {
      list: (q) => request<LinkPage>("/api/links", { query: { ...q } }),
      confirm: (id) => request<LinkResponse>(`/api/links/${enc(id)}/confirm`, { method: "POST" }),
      reject: (id) => request<LinkResponse>(`/api/links/${enc(id)}/reject`, { method: "POST" }),
      undo: (id) => request<void>(`/api/links/${enc(id)}`, { method: "DELETE" }),
      reconcile: () => request<ReconcileResult>("/api/links/reconcile", { method: "POST" }),
    },
  };
}
