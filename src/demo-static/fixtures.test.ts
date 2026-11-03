import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { DEMO_FIXTURES as f, FIXTURES_MARKER } from "./fixtures";

const MONEY_KEYS = new Set(["amount", "balance", "total", "income", "expenses", "net", "declaredTotal"]);
const MONEY_RE = /^-?[0-9]{1,16}(\.[0-9]{1,2})?$/;
const TEXT_KEYS = new Set(["name", "institution", "description", "originalDescription", "counterpartyName", "categoryName"]);

function walk(v: unknown, visit: (key: string, value: unknown, path: string[]) => void, path: string[] = []): void {
  if (Array.isArray(v)) {
    v.forEach((x) => walk(x, visit, path));
  } else if (v && typeof v === "object") {
    for (const [k, x] of Object.entries(v)) {
      visit(k, x, path);
      walk(x, visit, [...path, k]);
    }
  }
}

/** Forma do campo de dinheiro pelo contexto (raiz e chaves ancestrais). */
function moneyShape(key: string, path: string[]): string {
  if (key === "total") return path[path.length - 1] === "pending" ? "pending.total" : path.includes("byCategory") ? "byCategory[].total" : "total";
  if (key === "amount") {
    if (path[0] === "transactions") return "transaction.amount";
    if (path[0] === "checkpoints") return "checkpoint.amount";
    if (path[0] === "links") return "link.amount";
    if (path.includes("lines")) return "importLine.amount";
    return "amount";
  }
  if (key === "balance") return path[0] === "accounts" ? "account.balance" : "balance";
  return key; // declaredTotal, income, expenses, net
}

const REQUIRED_SHAPES = [
  "declaredTotal",
  "pending.total",
  "byCategory[].total",
  "income",
  "expenses",
  "net",
  "transaction.amount",
  "account.balance",
  "checkpoint.amount",
  "importLine.amount",
  "link.amount",
];

describe("fixtures do demo", () => {
  it("meta_is_the_reference_set", () => {
    expect(f.meta.generator).toBe(FIXTURES_MARKER);
    expect(f.meta.referenceDate).toBe("2026-10-15");
    expect(f.meta.months).toEqual(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"]);
    for (const m of f.meta.months) expect(f.summaries).toHaveProperty([m]);
  });

  it("every_money_field_is_a_contract_string", () => {
    const seen = new Map<string, number>();
    walk(f, (k, v, path) => {
      if (!MONEY_KEYS.has(k)) return;
      const shape = moneyShape(k, path);
      seen.set(shape, (seen.get(shape) ?? 0) + 1);
      expect(typeof v, `${shape} em ${path.join(".")}`).toBe("string");
      expect(v as string, `${shape} em ${path.join(".")}`).toMatch(MONEY_RE);
    });
    for (const shape of REQUIRED_SHAPES) expect(seen.get(shape) ?? 0, shape).toBeGreaterThan(0);
  });

  it("no_transaction_after_reference_date", () => {
    for (const t of f.transactions) expect(t.date <= f.meta.referenceDate).toBe(true);
  });

  it("only_synthetic_identity", () => {
    expect(f.meta.user.email).toBe("visitante@exemplo.invalid");
    walk(f, (k, v) => {
      if (!TEXT_KEYS.has(k) || typeof v !== "string") return;
      expect(v).not.toMatch(/\d{3}\.\d{3}\.\d{3}-\d{2}/);
      expect(v).not.toMatch(/\d{11,}/);
      expect(v).not.toMatch(/\S+@\S+/);
    });
  });

  it("links_cover_each_kind_and_a_suggestion", () => {
    const kinds = f.links.confirmed.map((l) => l.kind).sort();
    expect(kinds).toEqual(["InvoicePayment", "InvoicePayment", "OwnTransfer", "OwnTransfer", "PixCredit"]);
    expect(f.links.suggested.length).toBeGreaterThanOrEqual(2);
    expect(f.links.suggested.every((l) => l.kind === "OwnTransfer")).toBe(true);
    expect(f.links.confirmed.every((l) => l.state === "Confirmed")).toBe(true);
    expect(f.links.suggested.every((l) => l.state === "Suggested")).toBe(true);
    // O selo de cada linha aponta para o mesmo vínculo da lista (contraparte nula nos vínculos de lado vazio).
    const byId = new Map([...f.links.confirmed, ...f.links.suggested].map((l) => [l.id, l]));
    for (const l of byId.values()) {
      for (const [end, other] of [[l.out, l.in], [l.in, l.out]] as const) {
        if (!end) continue;
        const t = f.transactions.find((x) => x.id === end.transactionId)!;
        expect(t.link, end.transactionId).toMatchObject({ id: l.id, kind: l.kind, state: l.state, counterpartTransactionId: other?.transactionId ?? null });
      }
    }
  });

  it("links_include_one_sided_examples", () => {
    const oneSided = (l: { out: unknown; in: unknown }) => l.out === null || l.in === null;
    const confirmed = f.links.confirmed.filter(oneSided);
    expect(confirmed.map((l) => l.kind).sort()).toEqual(["InvoicePayment", "OwnTransfer"]);
    expect(confirmed.every((l) => l.external)).toBe(true);
    const suggested = f.links.suggested.filter(oneSided);
    expect(suggested).toHaveLength(1);
    expect(suggested[0].external).toBe(true);
    expect(f.links.confirmed.filter((l) => !oneSided(l)).every((l) => !l.external)).toBe(true);
  });

  it("transactions_carry_pairing_and_unlinked_pix_and_boleto_stay_normal", () => {
    const pairings = new Set(f.transactions.map((t) => t.pairing).filter(Boolean));
    expect([...pairings].sort()).toEqual(["InvoicePayment", "OwnTransfer", "PixCredit"]);
    for (const t of f.transactions) expect(t, t.id).toHaveProperty("pairing");
    const boleto = f.transactions.find((t) => t.description === "Boleto Condomínio Exemplo")!;
    expect([boleto.state, boleto.link, boleto.pairing]).toEqual(["Normal", null, null]);
    // Pix de terceiro: Normal como no importador real, ainda elegível ao "é meu".
    const third = f.transactions.find((t) => t.description === "Pix para Fornecedor Fictício")!;
    expect([third.state, third.link, third.pairing]).toEqual(["Normal", null, "OwnTransfer"]);
  });

  it("preview_batch_exists_in_preview_state", () => {
    expect(f.importDetails[f.meta.previewBatchId]?.state).toBe("Preview");
  });

  it("json_has_lf_only", () => {
    const dir = join(__dirname, "fixtures");
    const files = readdirSync(dir).filter((n) => n.endsWith(".json"));
    expect(files.length).toBeGreaterThan(0);
    for (const n of files) expect(readFileSync(join(dir, n), "utf8"), n).not.toContain("\r");
  });
});
