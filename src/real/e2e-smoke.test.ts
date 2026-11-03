import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory } from "vue-router";
import i18n from "./i18n";
import RealApp from "./RealApp.vue";
import { createRealRouter } from "./router";
import { setApi } from "./api/context";
import { setRouter } from "./routerRef";
import type { ApiClient } from "./api";
import type { Account, Category, ConfirmBody, ConfirmResult, ImportBatch, ImportLine, ImportPreview, Summary, Transaction, TransactionQuery } from "./api/types";
import { invoicePreview, line, ofx, preview, xlsx } from "./testingImport";
import { acc } from "./testing";
import { tx } from "./testingTx";

// Gráficos dependem de canvas (inexistente no jsdom); o resto da Visão geral é real.
vi.mock("vue-chartjs", async () => {
  const { defineComponent, h } = await import("vue");
  const make = (name: string) =>
    defineComponent({
      name,
      props: ["data", "options"],
      setup(_, { expose }) {
        expose({ chart: { update() {} } });
        return () => h("canvas");
      },
    });
  return { Bar: make("bar"), Doughnut: make("doughnut") };
});

// ---------------------------------------------------------------------------------------------------------------------
// API simulada e COERENTE: um pequeno servidor em memória. O resumo é calculado aqui (o app não faz aritmética).
// Toda a matemática abaixo é do FAKE (centavos inteiros), nunca do app.
// ---------------------------------------------------------------------------------------------------------------------
const cents = (s: string) => {
  const m = /^(-?)(\d+)\.(\d{2})$/.exec(s)!;
  return (m[1] ? -1 : 1) * (Number(m[2]) * 100 + Number(m[3]));
};
const money = (c: number) => `${c < 0 ? "-" : ""}${Math.floor(Math.abs(c) / 100)}.${String(Math.abs(c) % 100).padStart(2, "0")}`;

const CATEGORIES: Category[] = [
  { id: "c-mkt", name: "Mercado", kind: "Expense" } as Category,
  { id: "c-trp", name: "Transporte", kind: "Expense" } as Category,
  { id: "c-sal", name: "Salário", kind: "Income" } as Category,
];

const FILE_LINES: ImportLine[] = [
  line({ sourceKey: "n1", date: "2026-03-05", description: "Mercado Exemplo", amount: "-25.50" }),
  line({ sourceKey: "n2", date: "2026-03-06", description: "Salário Exemplo", amount: "1000.00" }),
  line({ sourceKey: "p1", date: "2026-03-07", description: "Transferência Exemplo", amount: "-40.00", state: "PendingClassification" }),
  line({ sourceKey: "p2", date: "2026-03-08", description: "Pix Misterioso", amount: "-12.00", state: "PendingClassification" }),
  line({ sourceKey: "e1", date: "2026-02-27", description: "Antiga Exemplo", amount: "-5.00", existing: true }),
];

function createServer() {
  let logged = false;
  const accounts: Account[] = [];
  const txs: (Transaction & { batchId?: string; sourceKey?: string })[] = [];
  const batches = new Map<string, ImportPreview>();
  const confirmCalls: { id: string; body: ConfirmBody }[] = [];
  const previewCalls: { accountId: string; fileName: string }[] = [];
  let seq = 0;

  const balanceOf = (id: string) => money(txs.filter((t) => t.accountId === id).reduce((s, t) => s + cents(t.amount), 0));
  const withBalance = (a: Account): Account => ({ ...a, balance: balanceOf(a.id) });

  const summaryOf = (month: string): Summary => {
    const inMonth = txs.filter((t) => t.date.startsWith(month));
    const normal = inMonth.filter((t) => t.state === "Normal");
    const pending = inMonth.filter((t) => t.state === "PendingClassification");
    const income = normal.filter((t) => cents(t.amount) > 0).reduce((s, t) => s + cents(t.amount), 0);
    const expenses = normal.filter((t) => cents(t.amount) < 0).reduce((s, t) => s + cents(t.amount), 0);
    const byCat = new Map<string | null, number>();
    for (const t of normal.filter((t) => cents(t.amount) < 0)) byCat.set(t.categoryId, (byCat.get(t.categoryId) ?? 0) + cents(t.amount));
    return {
      month,
      income: money(income),
      expenses: money(expenses),
      net: money(income + expenses),
      byCategory: [...byCat].map(([id, total]) => ({
        categoryId: id,
        categoryName: id ? (CATEGORIES.find((c) => c.id === id)?.name ?? null) : null,
        total: money(total),
      })),
      pending: { count: pending.length, total: money(pending.reduce((s, t) => s + cents(t.amount), 0)) },
    } as Summary;
  };

  const api = {
    auth: {
      me: async () => (logged ? { email: "user@example.test" } : null),
      login: async () => {
        logged = true;
      },
      logout: async () => {
        logged = false;
      },
    },
    accounts: {
      list: async () => accounts.map(withBalance),
      create: async (b: { name: string; type: Account["type"]; institution?: string | null; externalIdentifier?: string | null }) => {
        const a: Account = {
          id: `a${accounts.length + 1}`,
          name: b.name,
          type: b.type,
          institution: b.institution ?? "",
          externalIdentifier: b.externalIdentifier ?? null,
          isArchived: false,
          balance: "0.00",
        };
        accounts.push(a);
        return withBalance(a);
      },
      update: async () => {
        throw new Error("não usado");
      },
      checkpoints: async () => [],
      addCheckpoint: async () => {
        throw new Error("não usado");
      },
    },
    categories: { list: async () => CATEGORIES },
    transactions: {
      list: async (q: TransactionQuery) => {
        const rows = txs
          .filter((t) => (q.accountId ? t.accountId === q.accountId : true))
          .filter((t) => (q.month ? t.date.startsWith(q.month) : true))
          .filter((t) => (q.categoryId ? t.categoryId === q.categoryId : true))
          .filter((t) => (q.state ? t.state === q.state : true))
          .sort((a, b) => (a.date === b.date ? b.id.localeCompare(a.id) : b.date.localeCompare(a.date)));
        const page = q.page ?? 1;
        const size = q.size ?? 50;
        return { items: rows.slice((page - 1) * size, page * size), page, size, total: rows.length };
      },
      create: async () => {
        throw new Error("não usado");
      },
      update: async (id: string, b: { categoryId?: string | null; state?: Transaction["state"] }) => {
        const t = txs.find((x) => x.id === id)!;
        if (b.categoryId !== undefined) t.categoryId = b.categoryId;
        if (b.state) t.state = b.state;
        return t;
      },
      remove: async () => undefined,
    },
    summary: { month: async (m: string) => summaryOf(m) },
    imports: {
      preview: async (accountId: string, file: File) => {
        previewCalls.push({ accountId, fileName: file.name });
        const id = `b${++seq}`;
        if (file.name.endsWith(".xlsx")) {
          const base = invoicePreview().lines.map((l) => ({ ...l, state: "PendingClassification" as const }));
          const ip = invoicePreview({ batchId: id, accountId, lines: base });
          batches.set(id, ip);
          return ip;
        }
        const lines = FILE_LINES.map((l) => ({ ...l, existing: l.existing || txs.some((t) => t.sourceKey === l.sourceKey) }));
        const p = preview({ batchId: id, accountId, lines, editedCount: 0 });
        batches.set(id, p);
        return p;
      },
      list: async (): Promise<ImportBatch[]> =>
        [...batches.values()].map((p) => ({
          batchId: p.batchId,
          accountId: p.accountId,
          source: p.source,
          state: p.state,
          periodStart: p.periodStart,
          periodEnd: p.periodEnd,
          newCount: p.newCount,
          existingCount: p.existingCount,
          createdAt: p.createdAt,
          expiresAt: p.expiresAt,
          invoice: p.invoice,
        })),
      get: async (id: string) => {
        const p = batches.get(id);
        if (!p) throw Object.assign(new Error("404"), { status: 404 });
        return p;
      },
      confirm: async (id: string, body: ConfirmBody): Promise<ConfirmResult> => {
        confirmCalls.push({ id, body });
        const p = batches.get(id)!;
        let created = 0;
        for (const l of p.lines.filter((x) => !x.existing)) {
          const o = body.overrides?.find((x) => x.sourceKey === l.sourceKey);
          txs.push({
            ...tx({
              id: `t${txs.length + 1}`,
              accountId: p.accountId,
              date: l.date,
              amount: l.amount,
              description: l.description,
              originalDescription: l.description,
              categoryId: o?.categoryId ?? null,
              state: o?.state ?? l.state,
              origin: "Imported",
            }),
            batchId: id,
            sourceKey: l.sourceKey,
          });
          created++;
        }
        batches.set(id, { ...p, state: "Confirmed", expiresAt: null });
        return { batchId: id, created, skipped: p.lines.length - created, linked: 0, suggested: 0 };
      },
      undo: async (id: string) => {
        const p = batches.get(id)!;
        if (p.state === "Confirmed") {
          for (let i = txs.length - 1; i >= 0; i--) if (txs[i].batchId === id) txs.splice(i, 1);
          batches.set(id, { ...p, state: "Undone" });
        } else batches.delete(id);
      },
    },
  };
  return { api: api as unknown as ApiClient, txs, accounts, confirmCalls, previewCalls };
}

// ---------------------------------------------------------------------------------------------------------------------
// Auxiliares de teste
// ---------------------------------------------------------------------------------------------------------------------
const MONEY_LIKE = /\d[.,]\d{2}(?!\d)/;
const AMOUNT_TOKENS = ["25,50", "1.000,00", "65,50", "934,50", "40,00", "12,00", "5,00"];

/** Nenhum texto de dinheiro em texto, atributos ou título do documento. */
function expectNoMoney(w: VueWrapper) {
  const root = w.element as HTMLElement;
  const all = [root, ...Array.from(root.querySelectorAll<HTMLElement>("*"))];
  const haystacks: string[] = [root.textContent ?? "", document.title];
  for (const el of all) for (const a of Array.from(el.attributes)) haystacks.push(a.value);
  for (const h of haystacks) {
    expect(h).not.toMatch(MONEY_LIKE);
    for (const tok of AMOUNT_TOKENS) expect(h).not.toContain(tok);
  }
}

async function go(w: VueWrapper, path: string) {
  await w.find(`nav a[href='${path}']`).trigger("click");
  await flushPromises();
}

async function pickFile(w: VueWrapper, files: File[]) {
  const input = w.find("input[type=file]");
  Object.defineProperty(input.element, "files", { value: files, configurable: true });
  await input.trigger("change");
}

async function upload(w: VueWrapper, accountId: string) {
  await w.find("[data-test=account]").setValue(accountId);
  await pickFile(w, [ofx("extrato.ofx")]);
  await w.find("[data-test=upload-form]").trigger("submit");
  await flushPromises();
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-03-20T12:00:00"));
  i18n.global.locale.value = "pt-BR";
  localStorage.clear();
  document.title = "";
});

afterEach(() => {
  vi.useRealTimers();
  setApi(null);
  setRouter(null);
});

describe("Modo real: fluxo ponta a ponta com API simulada", () => {
  it("login -> conta -> importar com override -> resumo -> esconder valores -> desfazer", async () => {
    const server = createServer();
    const pinia = createPinia();
    setActivePinia(pinia);
    setApi(server.api);
    const router = createRealRouter(createMemoryHistory());

    // anônimo: / redireciona para /login com next
    await router.push("/");
    await router.isReady();
    expect(router.currentRoute.value.name).toBe("login");
    expect(router.currentRoute.value.query.next).toBe("/");

    const w = mount(RealApp, { global: { plugins: [pinia, router, i18n] } });
    await flushPromises();
    expect(w.find("nav").exists()).toBe(false);

    // login
    await w.find("#login-email").setValue("user@example.test");
    await w.find("#login-password").setValue("senha-de-teste");
    await w.find("form").trigger("submit");
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/");
    expect(w.find("[data-testid=user-email]").text()).toBe("user@example.test");

    // Visão geral vazia
    expect(w.find("[data-test=empty-state]").exists()).toBe(true);

    // criar conta
    await go(w, "/accounts");
    await w.find("#acc-name").setValue("Conta Exemplo");
    await w.find("form").trigger("submit");
    await flushPromises();
    expect(server.accounts).toHaveLength(1);
    expect(w.find("[data-test=account]").text()).toContain("Conta Exemplo");
    const accountId = server.accounts[0].id;

    // importar: enviar, classificar uma pendente, confirmar
    await go(w, "/import");
    await upload(w, accountId);
    expect(server.previewCalls).toEqual([{ accountId, fileName: "extrato.ofx" }]);
    const tabs = w.findAll("[role=tab]").map((t) => t.text());
    expect(tabs[0]).toMatch(/Novos.*2/);
    expect(tabs[1]).toMatch(/Pendentes.*2/);
    expect(tabs[2]).toMatch(/Já existentes.*1/);

    await w.find("#imp-tab-pending").trigger("click");
    const pendLines = w.findAll("[data-test=pending-line]");
    expect(pendLines).toHaveLength(2);
    expect(pendLines[0].text()).toContain("Transferência Exemplo");
    await pendLines[0].find("[data-test=line-state]").setValue("Normal");
    await pendLines[0].find("[data-test=line-category]").setValue("c-mkt");
    expect(w.find("[data-test=confirm]").text()).toBe("Confirmar 4 novos");
    await w.find("[data-test=confirm]").trigger("click");
    await flushPromises();

    // o servidor recebeu o override SÓ da linha classificada
    expect(server.confirmCalls).toHaveLength(1);
    expect(server.confirmCalls[0].body.overrides).toEqual([{ sourceKey: "p1", state: "Normal", categoryId: "c-mkt" }]);
    expect(w.find("[data-test=result]").text()).toContain("4 lançamentos criados, 1 pulados");
    expect(w.find("[data-test=history-row]").text()).toContain("Conta Exemplo");

    // Visão geral com valores do servidor
    await go(w, "/");
    const cards = w.find("[data-test=summary-cards]").text();
    expect(cards).toContain("1.000,00");
    expect(cards).toContain("65,50");
    expect(cards).toContain("934,50");
    expect(w.find("[data-test=month-pending]").text()).toContain("1 pendente");
    expect(w.find("[data-test=empty-state]").exists()).toBe(false);

    // Lançamentos
    await go(w, "/transactions");
    const rows = w.findAll("[data-test=tx-row]");
    expect(rows).toHaveLength(4);
    const text = rows.map((r) => r.text()).join("\n");
    for (const d of ["Mercado Exemplo", "Salário Exemplo", "Transferência Exemplo", "Pix Misterioso"]) expect(text).toContain(d);

    // Esconder valores: nada de dinheiro em Visão geral, Lançamentos, Pendentes e Importar
    await w.find("[data-testid=toggle-values]").trigger("click");
    expect(localStorage.getItem("fd.hideValues")).toBe("1");
    expectNoMoney(w);

    await go(w, "/");
    expect(w.find("[data-test=summary-cards]").exists()).toBe(true);
    expectNoMoney(w);

    await go(w, "/pending");
    expect(w.findAll("[data-test=pending-row]")).toHaveLength(1);
    expect(w.text()).toContain("Pix Misterioso");
    expectNoMoney(w);

    await go(w, "/transactions");
    expect(w.findAll("[data-test=tx-row]")).toHaveLength(4);
    expectNoMoney(w);

    await go(w, "/import");
    await upload(w, accountId); // todas as linhas agora já existem
    expect(w.find("[role=tablist]").exists()).toBe(true);
    expect(w.text()).toContain("Mercado Exemplo"); // linhas existem no DOM (aba oculta), sem dinheiro
    expectNoMoney(w);

    // voltar a mostrar
    await w.find("[data-testid=toggle-values]").trigger("click");
    expect(localStorage.getItem("fd.hideValues")).toBe("0");
    expect(w.text()).toContain("25,50");

    // cancelar a segunda prévia (não desfaz o lote confirmado)
    await w.find("[data-test=cancel]").trigger("click");
    await flushPromises();
    expect(w.find("[role=tablist]").exists()).toBe(false);
    expect(server.txs).toHaveLength(4);

    // desfazer o lote confirmado pelo histórico
    const undoButtons = w.findAll("[data-test=undo]");
    expect(undoButtons).toHaveLength(1);
    await undoButtons[0].trigger("click");
    await flushPromises();
    await w.find("[data-test=undo-yes]").trigger("click");
    await flushPromises();
    expect(server.txs).toHaveLength(0);
    expect(w.findAll("[data-test=undo]")).toHaveLength(0);

    await go(w, "/transactions");
    expect(w.find("[data-test=empty]").exists()).toBe(true);
    expect(w.text()).not.toContain("Mercado Exemplo");
    await go(w, "/");
    expect(w.find("[data-test=empty-state]").exists()).toBe(true);
  });

  it("importa fatura .xlsx: parcela, cartão, override por ordinal, total escondido", async () => {
    const server = createServer();
    const pinia = createPinia();
    setActivePinia(pinia);
    setApi(server.api);
    const router = createRealRouter(createMemoryHistory());
    await router.push("/import");
    await router.isReady();
    const w = mount(RealApp, { global: { plugins: [pinia, router, i18n] } });
    await flushPromises();
    await w.find("#login-email").setValue("user@example.test");
    await w.find("#login-password").setValue("senha-de-teste");
    await w.find("form").trigger("submit");
    await flushPromises();
    server.accounts.push(acc({ id: "cc1", name: "Cartão Exemplo", type: "CreditCard" }));
    await go(w, "/accounts");
    await go(w, "/import");

    await w.find("[data-test=account]").setValue("cc1");
    await pickFile(w, [xlsx("fatura.xlsx")]);
    await w.find("[data-test=upload-form]").trigger("submit");
    await flushPromises();
    expect(server.previewCalls).toEqual([{ accountId: "cc1", fileName: "fatura.xlsx" }]);

    expect(w.text()).toContain("10/04/2026");
    expect(w.text()).toContain("150,00");
    expect(w.text()).toContain("Parcela 1 de 3");
    expect(w.text()).toContain("•••• 1234");

    // duas linhas pendentes com a mesma chave: o select da segunda gera override só no ordinal 1
    await w.find("#imp-tab-pending").trigger("click");
    const rows = w.findAll("[data-test=pending-line]");
    expect(rows).toHaveLength(2);
    await rows[1].find("[data-test=line-state]").setValue("Normal");
    await w.find("[data-test=confirm]").trigger("click");
    await flushPromises();
    expect(server.confirmCalls).toHaveLength(1);
    expect(server.confirmCalls[0].body.overrides).toEqual([{ sourceKey: "i1", ordinal: 1, state: "Normal" }]);

    await w.find("[data-testid=toggle-values]").trigger("click");
    expect(w.find("[data-test=history-row]").exists()).toBe(true);
    expectNoMoney(w);
  });
});
