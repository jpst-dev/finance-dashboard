// Garante que cada build carrega só o código do seu modo: o demo estático não pode
// ter cliente HTTP real e o build real não pode ter fixtures nem o marcador do demo.
// Nunca imprime conteúdo dos arquivos, apenas pasta/arquivo e o marcador.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

export const RULES = [
  {
    dir: "dist",
    required: ["data-finance-demo-mode", "finance-demo-fixtures"],
    forbidden: ["data-finance-real-mode", "/api/auth/csrf"],
  },
  {
    dir: "dist-real",
    required: ["data-finance-real-mode"],
    forbidden: ["data-finance-demo-mode", "finance-demo-fixtures"],
  },
];

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) yield* walk(p);
    else if (/\.(js|mjs|css|html|map)$/.test(name)) yield p;
  }
}

export function checkBundles(root, rules = RULES) {
  const messages = [];
  for (const { dir, required, forbidden } of rules) {
    const full = join(root, dir);
    if (!existsSync(full)) {
      messages.push(`FALHA: pasta ${dir}/ não existe (rode o build antes).`);
      continue;
    }
    const files = [...walk(full)];
    if (files.length === 0) {
      messages.push(`FALHA: pasta ${dir}/ não tem arquivos de bundle (build vazio?).`);
      continue;
    }
    const found = new Set();
    for (const file of files) {
      const text = readFileSync(file, "utf8");
      const shown = file.slice(root.length).replace(/^[\/]+/, "").replaceAll("\\", "/");
      for (const marker of forbidden) {
        if (text.includes(marker)) messages.push(`FALHA: ${shown} contém o marcador proibido "${marker}".`);
      }
      for (const marker of required) if (text.includes(marker)) found.add(marker);
    }
    for (const marker of required) {
      if (!found.has(marker)) messages.push(`FALHA: ${dir}/ não contém o marcador obrigatório "${marker}" (build do modo errado?).`);
    }
  }
  return { ok: messages.length === 0, messages };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { ok, messages } = checkBundles(process.cwd());
  for (const m of messages) console.error(m);
  if (ok) console.log("ok: bundles dist/ e dist-real/ isolados por modo");
  process.exit(ok ? 0 : 1);
}
