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
  /(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)["'`]([^"'`]+)["'`]/g;

function specifiersOf(text: string): string[] {
  return [...text.matchAll(IMPORT_RE)].map((m) => m[1]);
}

function specifiers(file: string): string[] {
  return specifiersOf(readFileSync(file, "utf8"));
}

// Resolve um especificador para um caminho absoluto: relativo ("./x"), alias
// "@/" (= src/, como em vite.config e tsconfig) ou null (pacote externo).
function resolveSpecifier(from: string, spec: string): string | null {
  if (spec.startsWith("@/")) return resolve(SRC, spec.slice(2));
  if (spec.startsWith(".")) return resolve(dirname(from), spec);
  return null;
}

function isWithin(target: string, root: string): boolean {
  return target === root || target.startsWith(root + sep);
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
        const target = resolveSpecifier(f, s);
        if (target === null) continue;
        if (!isWithin(target, realRoot)) {
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
        const target = resolveSpecifier(f, s);
        if (target === null) continue;
        const rel = relative(SRC, target).split(sep).join("/");
        if (/^real\/boot(\.ts)?$/.test(rel) || /^real\/api(\/index|\/http)?(\.ts)?$/.test(rel)) {
          offenders.push(`${relative(SRC, f)} -> ${s}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it("resolves_alias_and_backtick_imports", () => {
    const realRoot = join(SRC, "real");
    const from = join(realRoot, "pages", "X.vue");
    const text = [
      'import a from "@/store/x";',
      "const b = await import(`../../demo-static/y`);",
      'import c from "./ok";',
      'import d from "vue";',
    ].join(" ");
    const specs = specifiersOf(text);
    expect(specs).toEqual(["@/store/x", "../../demo-static/y", "./ok", "vue"]);
    const outside = specs
      .map((s) => resolveSpecifier(from, s))
      .filter((t): t is string => t !== null && !isWithin(t, realRoot));
    expect(outside).toEqual([join(SRC, "store", "x"), join(SRC, "demo-static", "y")]);
    expect(resolveSpecifier(from, "vue")).toBeNull();
    expect(resolveSpecifier(from, "@/real/api/index")).toBe(join(SRC, "real", "api", "index"));
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
