export type AppMode = "demo" | "real";

/** Só o valor exato "real" ativa o modo real; qualquer outro cai no demo. */
export function resolveMode(env: { VITE_APP_MODE?: string }): AppMode {
  return env.VITE_APP_MODE === "real" ? "real" : "demo";
}

export const APP_MODE: AppMode = resolveMode(import.meta.env);
