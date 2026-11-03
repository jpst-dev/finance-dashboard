import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { DEMO_FIXTURES as f, FIXTURES_MARKER } from "./fixtures";

const MONEY_KEYS = new Set(["amount", "balance", "total", "income", "expenses", "net", "declaredTotal"]);
const MONEY_RE = /^-?[0-9]{1,16}(\.[0-9]{1,2})?$/;
const TEXT_KEYS = new Set(["name", "institution", "description", "originalDescription", "counterpartyName", "categoryName"]);

function walk(v: unknown, visit: (key: string, value: unknown) => void): void {
  if (Array.isArray(v)) {
    v.forEach((x) => walk(x, visit));
  } else if (v && typeof v === "object") {
    for (const [k, x] of Object.entries(v)) {
      visit(k, x);
      walk(x, visit);
    }
  }
}

describe("fixtures do demo", () => {
  it("meta_is_the_reference_set", () => {
    expect(f.meta.generator).toBe(FIXTURES_MARKER);
    expect(f.meta.referenceDate).toBe("2026-10-15");
    expect(f.meta.months).toEqual(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"]);
    for (const m of f.meta.months) expect(f.summaries).toHaveProperty([m]);
  });

  it("every_money_field_is_a_contract_string", () => {
    let seen = 0;
    walk(f, (k, v) => {
      if (!MONEY_KEYS.has(k)) return;
      if (v === null) return; // declaredTotal etc. podem ser nulos no contrato
      seen++;
      expect(typeof v, k).toBe("string");
      expect(v as string, k).toMatch(MONEY_RE);
    });
    expect(seen).toBeGreaterThan(50);
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
