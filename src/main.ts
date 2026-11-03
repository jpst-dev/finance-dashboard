import "./assets/main.css";
// A comparação literal com import.meta.env é substituída em build, então o Rollup
// elimina o ramo (e o chunk dinâmico) do modo não selecionado. Equivale a
// resolveMode(import.meta.env) === "real" (src/real/mode.ts), que não é dobrável em build.
if (import.meta.env.VITE_APP_MODE === "real") {
  import("./real/boot").then((m) => m.bootReal());
} else {
  import("./demo/boot").then((m) => m.bootDemo());
}
