// Garante que cada build não carrega código do outro modo.
// Nunca imprime conteúdo dos arquivos, apenas o nome do arquivo infrator.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

// Nota: "X-XSRF-TOKEN" não serve de marcador do modo real, pois o axios (usado pelo
// legado) o embute por padrão. O marcador real é definido em src/real/boot.ts.
const REAL_MARKER = "data-finance-real-mode";
const checks = [
  { dir: "dist", forbidden: REAL_MARKER, required: null, why: "código do modo real no bundle demo" },
  { dir: "dist-real", forbidden: "finance_dashboard_transactions", required: REAL_MARKER, why: "código do modo demo no bundle real" },
];

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) yield* walk(p);
    else if (/\.(js|mjs|css|html|map)$/.test(name)) yield p;
  }
}

let failed = false;
for (const { dir, forbidden, required, why } of checks) {
  if (!existsSync(dir)) {
    console.error(`FALHA: pasta ${dir}/ não existe (rode o build antes).`);
    failed = true;
    continue;
  }
  let foundRequired = required === null;
  for (const file of walk(dir)) {
    const text = readFileSync(file, "utf8");
    if (text.includes(forbidden)) {
      console.error(`FALHA: ${file} contém "${forbidden}" (${why}).`);
      failed = true;
    }
    if (required && text.includes(required)) foundRequired = true;
  }
  if (!foundRequired) {
    console.error(`FALHA: ${dir}/ não contém o marcador "${required}" (build do modo errado?).`);
    failed = true;
  }
}
if (!failed) console.log("ok: bundles dist/ e dist-real/ isolados por modo");
process.exit(failed ? 1 : 0);
