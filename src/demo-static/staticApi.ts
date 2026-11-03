// Adaptador estático de ApiClient: lê só das fixtures, nunca usa rede.
// Não importar ../real/api/http nem ../real/api/index (o bundle do demo não pode conter o cliente HTTP).
import type { ApiClient } from "../real/api/client";
import { ApiError, DEMO_READ_ONLY_CODE } from "../real/api/errors";
import type { DemoFixtures } from "./fixtures";

const MONTH_RE = /^\d{4}-(0[1-9]|1[0-2])$/;
const DEFAULT_SIZE = 50;
const MAX_SIZE = 200;

const clone = <T>(v: T): T => structuredClone(v);
const own = (o: object, k: string): boolean => Object.prototype.hasOwnProperty.call(o, k);
const readOnly = (): Promise<never> => Promise.reject(new ApiError(403, DEMO_READ_ONLY_CODE));

export function createStaticApi(f: DemoFixtures): ApiClient {
  return {
    auth: {
      me: async () => clone(f.meta.user),
      login: readOnly,
      logout: readOnly,
    },
    accounts: {
      list: async () => clone(f.accounts),
      create: readOnly,
      update: readOnly,
      checkpoints: async (id) => {
        const c = own(f.checkpoints, id) ? f.checkpoints[id] : undefined;
        if (!c) throw new ApiError(404);
        return clone(c);
      },
      addCheckpoint: readOnly,
    },
    categories: {
      list: async () => clone(f.categories),
    },
    transactions: {
      list: async (q) => {
        const page = q.page ?? 1;
        const size = Math.min(q.size ?? DEFAULT_SIZE, MAX_SIZE);
        if (page < 1 || size < 1) throw new ApiError(400);
        if (q.month !== undefined && !MONTH_RE.test(q.month)) throw new ApiError(400);
        const all = f.transactions.filter(
          (t) =>
            (q.accountId === undefined || t.accountId === q.accountId) &&
            (q.categoryId === undefined || t.categoryId === q.categoryId) &&
            (q.state === undefined || t.state === q.state) &&
            (q.month === undefined || t.date.startsWith(q.month)),
        );
        const start = (page - 1) * size;
        return clone({ items: all.slice(start, start + size), page, size, total: all.length });
      },
      create: readOnly,
      update: readOnly,
      remove: readOnly,
    },
    summary: {
      month: async (m, accountId) => {
        if (accountId !== undefined) throw new Error("demo: resumo por conta não está nas fixtures");
        if (!MONTH_RE.test(m)) throw new ApiError(400);
        const s = own(f.summaries, m) ? f.summaries[m] : undefined;
        return s ? clone(s) : { ...clone(f.emptySummary), month: m };
      },
    },
    imports: {
      preview: async (_accountId, _file) => clone(f.importDetails[f.meta.previewBatchId]),
      list: async () => clone(f.imports),
      get: async (id) => {
        const d = own(f.importDetails, id) ? f.importDetails[id] : undefined;
        if (!d) throw new ApiError(404);
        return clone(d);
      },
      confirm: readOnly,
      undo: readOnly,
    },
  };
}
