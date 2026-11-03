import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { describe, expect, it } from "vitest";

const SRC = resolve(__dirname);
const LEGACY = [
  "App.vue",
  "router",
  "pages",
  "components",
  "store",
  "services",
  "utils",
  "i18n",
  "demo",
];

function walk(dir: string, exts: string[]): string[] {
  const out: string[] = [];
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) out.push(...walk(p, exts));
    else if (exts.some((e) => name.endsWith(e))) out.push(p);
  }
  return out;
}

const IMPORT_RE =
  /(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)["']([^"']+)["']/g;

function specifiers(file: string): string[] {
  const text = readFileSync(file, "utf8");
  return [...text.matchAll(IMPORT_RE)].map((m) => m[1]);
}

describe("fontes do projeto", () => {
  it("legacy_tree_is_gone", () => {
    for (const p of LEGACY) expect(existsSync(join(SRC, p)), p).toBe(false);
  });

  it("real_sources_import_only_within_src_real", () => {
    const realRoot = join(SRC, "real");
    const files = walk(realRoot, [".ts", ".vue"]).filter(
      (f) => f !== join(SRC, "sources.test.ts"),
    );
    expect(files.length).toBeGreaterThan(0);
    const offenders: string[] = [];
    for (const f of files) {
      for (const s of specifiers(f)) {
        if (!s.startsWith(".")) continue;
        const target = resolve(dirname(f), s);
        if (target !== realRoot && !target.startsWith(realRoot + sep)) {
          offenders.push(`${relative(SRC, f)} -> ${s}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it("demo_static_never_imports_http_client", () => {
    const files = walk(join(SRC, "demo-static"), [".ts", ".vue"]);
    expect(files.length).toBeGreaterThan(0);
    const offenders: string[] = [];
    for (const f of files) {
      if (f === join(SRC, "sources.test.ts")) continue;
      for (const s of specifiers(f)) {
        if (/real\/boot(\.ts)?$/.test(s) || /real\/api(\/index|\/http)?(\.ts)?$/.test(s)) {
          offenders.push(`${relative(SRC, f)} -> ${s}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it("no_legacy_dependency_is_imported", () => {
    const banned = /^(axios|lodash|vue-toastification)(\/|$)/;
    const offenders: string[] = [];
    for (const f of walk(SRC, [".ts", ".vue"])) {
      if (f === join(SRC, "sources.test.ts")) continue;
      for (const s of specifiers(f)) {
        if (banned.test(s)) offenders.push(`${relative(SRC, f)} -> ${s}`);
      }
    }
    expect(offenders).toEqual([]);
  });
});
