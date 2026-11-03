import { afterEach, describe, expect, it } from "vitest";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { checkBundles, RULES } from "./check-bundles.mjs";

const SENTINEL = "frase-sentinela-que-nunca-deve-vazar";
const roots = [];

function makeRoot(files) {
  const root = mkdtempSync(join(tmpdir(), "bundles-"));
  roots.push(root);
  for (const [rel, content] of Object.entries(files)) {
    const p = join(root, rel);
    mkdirSync(join(p, ".."), { recursive: true });
    writeFileSync(p, content);
  }
  return root;
}

const GOOD = {
  "dist/assets/app.js": `x("data-finance-demo-mode");y("finance-demo-fixtures");${SENTINEL}`,
  "dist-real/assets/app.js": `x("data-finance-real-mode");y("/api/auth/csrf")`,
};

afterEach(() => {
  while (roots.length) rmSync(roots.pop(), { recursive: true, force: true });
});

describe("checkBundles", () => {
  it("passes_when_markers_are_where_they_belong", () => {
    const r = checkBundles(makeRoot(GOOD));
    expect(r.messages).toEqual([]);
    expect(r.ok).toBe(true);
  });

  it("fails_when_demo_bundle_has_http_client", () => {
    const root = makeRoot({ ...GOOD, "dist/assets/http.js": 'fetch("/api/auth/csrf")' });
    const r = checkBundles(root);
    expect(r.ok).toBe(false);
    expect(r.messages.join("\n")).toContain("/api/auth/csrf");
    expect(r.messages.join("\n")).toContain("http.js");
  });

  it("fails_when_real_bundle_has_fixtures_or_demo_marker", () => {
    const a = checkBundles(makeRoot({ ...GOOD, "dist-real/assets/f.js": "finance-demo-fixtures" }));
    expect(a.ok).toBe(false);
    expect(a.messages.join("\n")).toContain("finance-demo-fixtures");
    const b = checkBundles(makeRoot({ ...GOOD, "dist-real/assets/d.js": "data-finance-demo-mode" }));
    expect(b.ok).toBe(false);
    expect(b.messages.join("\n")).toContain("data-finance-demo-mode");
  });

  it("fails_when_required_marker_missing", () => {
    const r = checkBundles(makeRoot({ ...GOOD, "dist/assets/app.js": "nada" }));
    expect(r.ok).toBe(false);
    expect(r.messages.join("\n")).toContain("data-finance-demo-mode");
  });

  it("fails_when_dir_missing", () => {
    const r = checkBundles(makeRoot({ "dist/assets/app.js": GOOD["dist/assets/app.js"] }));
    expect(r.ok).toBe(false);
    expect(r.messages.join("\n")).toContain("dist-real");
  });

  it("fails_when_dir_is_empty", () => {
    const root = makeRoot(GOOD);
    rmSync(join(root, "dist-real", "assets"), { recursive: true });
    const r = checkBundles(root);
    expect(r.ok).toBe(false);
  });

  it("messages_never_contain_file_content", () => {
    const root = makeRoot({ ...GOOD, "dist/assets/http.js": `fetch("/api/auth/csrf");${SENTINEL}` });
    const r = checkBundles(root);
    expect(r.ok).toBe(false);
    expect(r.messages.join("\n")).not.toContain(SENTINEL);
  });

  it("declares_the_documented_rules", () => {
    expect(RULES.map((x) => x.dir)).toEqual(["dist", "dist-real"]);
  });
});
